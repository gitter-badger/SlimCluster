namespace SlimCluster.Membership.Swim
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using SlimCluster.Membership.Swim.Messages;
    using SlimCluster.Membership.Swim.Serialization;
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading.Tasks;

    /// <summary>
    /// The SWIM algorithm implementation of <see cref="IClusterMembership"/> for maintaining membership.
    /// </summary>
    public class SwimClusterMembership : IClusterMembership, IAsyncDisposable
    {
        private readonly ILogger<SwimClusterMembership> logger;
        private readonly SwimClusterMembershipOptions options;
        private readonly ISerializer serializer;

        /// <summary>
        /// Other known members
        /// </summary>
        private readonly LinkedList<SwimMember> otherMembers;
        private readonly object otherMembersLock = new object();

        // This member state
        private readonly SwimMemberSelf memberSelf;

        public string ClusterId => options.ClusterId;

        public IReadOnlyCollection<IMember> Members => otherMembers;

        public event IClusterMembership.MemberJoinedEventHandler? MemberJoined;
        public event IClusterMembership.MemberLeftEventHandler? MemberLeft;

        public SwimClusterMembership(ILogger<SwimClusterMembership> logger, IOptions<SwimClusterMembershipOptions> options, ISerializer serializer)
        {
            this.logger = logger;
            this.serializer = serializer;
            this.options = options.Value;
            this.otherMembers = new LinkedList<SwimMember>();

            this.udpMulticastGroupAddress = IPAddress.Parse(this.options.UdpMulticastGroupAddress);

            // This member status
            this.memberSelf = new SwimMemberSelf(this.options.NodeId, 0);
        }

        private readonly object udpClientLock = new object();
        private UdpClient? udpClient;
        private readonly IPAddress udpMulticastGroupAddress;
        private bool isStarted = false;
        private bool recieveLoopCanRun = true;
        private Task? recieveLoopTask;

        public async Task Start()
        {
            if (!isStarted) {
                // See https://docs.microsoft.com/pl-pl/dotnet/api/system.net.sockets.udpclient.joinmulticastgroup?view=net-5.0

                // Join or create a multicast group
                lock (udpClientLock) {
                    udpClient = new UdpClient(options.UdpPort, options.AddressFamily);
                    udpClient.JoinMulticastGroup(udpMulticastGroupAddress);
                    //udpClient.Client.RemoteEndPoint
                }

                // Run the message processing loop
                recieveLoopCanRun = true;
                recieveLoopTask = Task.Factory.StartNew(() => RecieveLoop(), TaskCreationOptions.LongRunning);//.Unwrap();

                await NotifyJoined();

                isStarted = true;
            }
        }

        protected async Task NotifyJoined()
        {
            if (udpClient is null) throw new NullReferenceException(nameof(udpClient));

            // Announce (multicast) to others that this node joined the network
            var payload = serializer.Serialize(new NodeJoinedMessage(memberSelf.Id, memberSelf.Incarnation));
            var result = await udpClient.SendAsync(payload, payload.Length, new IPEndPoint(udpMulticastGroupAddress, options.UdpPort));
        }

        private async Task RecieveLoop()
        {
            logger.LogInformation("Running recieve loop...");

            try {
                while (recieveLoopCanRun && udpClient != null) {
                    var result = await udpClient.ReceiveAsync();

                    try {
                        var msg = serializer.Deserialize<NodeJoinedMessage>(result.Buffer);

                        if (msg != null && msg.NodeId != null) {
                            OnNodeJoined(msg, result.RemoteEndPoint);
                        }
                    }
                    catch (Exception e) {
                        logger.LogWarning(e, "Could not deserialize arriving message");
                    }
                }
            }
            catch (ObjectDisposedException) {
                // Intended: this is how it exists from ReceiveAsync
            }
            finally {
                logger.LogInformation("Recieve loop finished");
            }
        }

        public async Task Stop()
        {
            if (isStarted) {
                recieveLoopCanRun = false;

                // Stop multicast group
                lock (udpClientLock) {
                    if (udpClient != null) {
                        udpClient.DropMulticastGroup(udpMulticastGroupAddress);
                        udpClient.Dispose();
                        udpClient = null;
                    }
                }

                if (recieveLoopTask != null) {
                    try {
                        await recieveLoopTask;
                    } catch {

                    }
                    recieveLoopTask = null;
                }

                isStarted = false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Stop();
        }

        protected void OnNodeJoined(NodeJoinedMessage m, IPEndPoint endPoint)
        {
            logger.LogInformation("Node {NodeId} incarnation {Incarnation} joined at [{NodeAddress}]:{NodePort}", m.NodeId, m.Incarnation, endPoint.Address, endPoint.Port);

            // Add other members only
            if (m.NodeId != memberSelf.Id) {

                var member = new SwimMember(m.NodeId, new IPAddressWrapper(endPoint), DateTime.UtcNow, m.Incarnation, SwimMemberStatus.Active);
                lock (otherMembersLock) {
                    otherMembers.AddLast(member);
                }

                try {
                    MemberJoined?.Invoke(this, new MemberEventArgs(member.Node, member.LastSeen));
                }
                catch (Exception e) {
                    logger.LogWarning(e, "Exception while invoking event {HandlerName}", nameof(MemberJoined));
                }
            }
        }
    }

}
