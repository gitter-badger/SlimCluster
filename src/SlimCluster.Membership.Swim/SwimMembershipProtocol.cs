namespace SlimCluster.Membership.Swim
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading.Tasks;

    public class SwimMembershipProtocol : IClusterMembership, IAsyncDisposable
    {
        private readonly ILogger<SwimMembershipProtocol> logger;
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

        public SwimMembershipProtocol(ILogger<SwimMembershipProtocol> logger, IOptions<SwimClusterMembershipOptions> options, ISerializer serializer)
        {
            this.logger = logger;
            this.serializer = serializer;
            this.options = options.Value;
            this.otherMembers = new LinkedList<SwimMember>();

            this.groupAddress = IPAddress.Parse(this.options.UdpMulticastGroupAddress);

            // This member status
            this.memberSelf = new SwimMemberSelf(this.options.NodeId, 0);
        }

        private readonly object udpClientLock = new object();
        private UdpClient? udpClient;
        private readonly IPAddress groupAddress;
        private bool isStarted = false;
        private bool canRun = true;
        private Task? recieveLoopTask;

        public async Task Start()
        {
            if (!isStarted) {
                // See https://docs.microsoft.com/pl-pl/dotnet/api/system.net.sockets.udpclient.joinmulticastgroup?view=net-5.0

                // Join or create a multicast group
                lock (udpClientLock) {
                    udpClient = new UdpClient(options.UdpPort, AddressFamily.InterNetwork/* InterNetworkV6*/);
                    udpClient.JoinMulticastGroup(groupAddress);
                    //udpClient.Client.RemoteEndPoint
                }

                // Run the message processing loop
                canRun = true;
                recieveLoopTask = Task.Factory.StartNew(() => RecieveLoop(), TaskCreationOptions.LongRunning).Unwrap();

                await NotifyJoined();

                isStarted = true;
            }
        }

        protected async Task NotifyJoined()
        {
            if (udpClient is null) throw new NullReferenceException(nameof(udpClient));

            // Announce (multicast) to others that this node joined the network
            var payload = serializer.Serialize(new NodeJoinedMessage(memberSelf.Id, memberSelf.Incarnation));
            var result = await udpClient.SendAsync(payload, payload.Length, new IPEndPoint(groupAddress, options.UdpPort));
        }

        private async Task RecieveLoop()
        {
            logger.LogInformation("Running recieve loop...");

            try {
                while (canRun && udpClient != null) {
                    var result = await udpClient.ReceiveAsync();

                    var msg = serializer.Deserialize<NodeJoinedMessage>(result.Buffer);

                    if (msg != null && msg.NodeId != null) {
                        OnNodeJoined(msg, result.RemoteEndPoint);
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
                canRun = false;

                // Stop multicast group
                lock (udpClientLock) {
                    if (udpClient != null) {
                        udpClient.DropMulticastGroup(groupAddress);
                        udpClient.Dispose();
                        udpClient = null;
                    }
                }

                if (recieveLoopTask != null) {
                    await recieveLoopTask;
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
            logger.LogInformation("Node with {NodeId} incarnation {Incarnation} joined at [{NodeAddress}]:{NodePort}", m.NodeId, m.Incarnation, endPoint.Address, endPoint.Port);

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

    public class NodeJoinedMessage
    {
        [JsonProperty("id")]
        public string NodeId { get; set; }

        [JsonProperty("inc")]
        public int Incarnation { get; set; }

        protected NodeJoinedMessage()
        {
            NodeId = string.Empty;
        }
        public NodeJoinedMessage(string nodeId, int incarnation)
        {
            NodeId = nodeId;
            Incarnation = incarnation;
        }
    }

    public class SwimMemberSelf
    {
        public string Id { get; }
        public int Incarnation { get; set; }

        public SwimMemberSelf(string id, int incarnation)
        {
            Id = id;
            Incarnation = incarnation;
        }
    }

    public class SwimMember : IMember, INode
    {
        public string Id { get; }
        public IAddress Address { get; protected set; }
        public INodeStatus Status { get; protected set; }
        public int Incarnation { get; set; }
        /// <summary>
        /// Point in time after which the Suspicious node will be declared as Confirm if no ACK is recieved.
        /// </summary>
        public DateTime? SuspiciousTimeout { get; set; }
        public DateTime? LastPing { get; set; }

        #region IMember

        public INode Node => this;
        public DateTime Joined { get; protected set; }
        public DateTime LastSeen { get; protected set; }

        #endregion

        public SwimMember(string id, IAddress address, DateTime joined, int incarnation, SwimMemberStatus status)
        {
            Id = id;
            Incarnation = incarnation;
            Address = address;
            Status = status;

            Joined = joined;
            LastSeen = joined;
        }
    }

    public class SwimMemberStatus : INodeStatus
    {
        public Guid Id { get; }

        public string Name { get; }

        protected SwimMemberStatus(Guid id, string name)
        {
            Id = id;
            Name = name;
        }

        public override bool Equals(object? obj) => obj is SwimMemberStatus status && Id.Equals(status.Id);
        public override int GetHashCode() => HashCode.Combine(Id);
        public override string ToString() => Name;

        public static readonly SwimMemberStatus Active = new SwimMemberStatus(new Guid("{112D19E0-A0B3-4EDD-8A35-76F299204406}"), "Active");
        public static readonly SwimMemberStatus Suspicious = new SwimMemberStatus(new Guid("{98D19B73-1CCD-43D5-B3AC-CFFCD2A9305B}"), "Suspicious");
        public static readonly SwimMemberStatus Confirm = new SwimMemberStatus(new Guid("{0BB58A7C-8ABF-4125-BFE4-C7D9BF62F8D0}"), "Confirm");
    }

    public class IPAddressWrapper : IAddress
    {
        public IPEndPoint EndPoint { get; }

        public IPAddressWrapper(IPEndPoint endPoint)
        {
            EndPoint = endPoint;
        }
    }


    public class SwimClusterMembershipOptions
    {
        /// <summary>
        /// The logical cluster id that identifies this cluster.
        /// </summary>
        public string ClusterId { get; set; } = "MyClusterId";

        /// <summary>
        /// The unique ID representing this node instance
        /// </summary>
        public string NodeId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// The time period (T) at which the failure detection happen to every sub group of nodes (k).
        /// </summary>
        public TimeSpan Period { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// The sub-group size for the failure detection (k). This indicates how many nodes are pinged in every T cycle.
        /// </summary>
        public int SubgroupSize { get; set; } = 3;

        /// <summary>
        /// UDP port used for internal membership message exchange
        /// </summary>
        public int UdpPort { get; set; } = 60001;

        /// <summary>
        /// UDP multicast group on which new joining members will announce themselves.
        /// </summary>
        public string UdpMulticastGroupAddress { get; set; } = "239.1.1.1"; //"FF01::1";
    }

    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddClusterMembership(this IServiceCollection services, Action<SwimClusterMembershipOptions> options)
        {
            services.Configure(options);
            services.AddSingleton<SwimMembershipProtocol>();

            services.AddTransient<ISerializer>(svp => new JsonMessageSerializer(Encoding.ASCII));
            services.AddSingleton<IClusterMembership>(svp => svp.GetRequiredService<SwimMembershipProtocol>());

            return services;

        }
    }

}
