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
        }

        private readonly object udpClientLock = new object();
        private UdpClient? udpClient;
        private readonly IPAddress groupAddress;
        private bool isStarted = false;
        private bool canRun = true;
        private Task? recieveLoopTask;

        public async Task Start()
        {
            if (!isStarted)
            {
                // See https://docs.microsoft.com/pl-pl/dotnet/api/system.net.sockets.udpclient.joinmulticastgroup?view=net-5.0

                // Join or create a multicast group
                udpClient = new UdpClient(options.UdpPort, AddressFamily.InterNetworkV6);
                udpClient.JoinMulticastGroup(groupAddress);

                // Run the message processing loop
                canRun = true;
                recieveLoopTask = Task.Factory.StartNew(() => RecieveLoop(), TaskCreationOptions.LongRunning).Unwrap();

                // Announce that this node joined the network
                var payload = serializer.Serialize(new NodeJoinedMessage { NodeId = options.NodeId });
                var result = await udpClient.SendAsync(payload, payload.Length, new IPEndPoint(groupAddress, options.UdpPort));

                isStarted = true;
            }
            // Establish the communication endpoint.
            // ToDo: Multicast that this member joined.
        }

        private async Task RecieveLoop()
        {
            logger.LogInformation("Running recieve loop...");

            try {
                while (canRun && udpClient != null) {
                    var result = await udpClient.ReceiveAsync();

                    var msg = serializer.Deserialize<NodeJoinedMessage>(result.Buffer);

                    if (msg != null && msg.NodeId != null) {
                        await OnNodeJoined(msg.NodeId, result.RemoteEndPoint);
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
                // Stop multicast group
                if (udpClient != null) {
                    udpClient.DropMulticastGroup(groupAddress);
                    udpClient.Dispose();
                    udpClient = null;
                }

                canRun = false;
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

        protected async Task OnNodeJoined(string id, IPEndPoint address)
        {
            logger.LogInformation("Node with {NodeId} joined with address {NodeAddress}", id, address);

            // ToDo: Implement
        }
    }

    public class NodeJoinedMessage
    {
        [JsonProperty("id")]
        public string? NodeId { get; set; }
    }

    public class SwimMemberSelf : IMember, INode
    {
        public string Id { get; }
        public IAddress Address { get; protected set; }
        public INodeStatus Status { get; protected set; }
        public int Incarnation { get; set; }

        #region IMember

        public INode Node => this;
        public DateTime Joined { get; protected set; }
        public DateTime LastSeen { get; protected set; }

        #endregion

        public SwimMemberSelf(string id, IAddress address, int incarnation)
        {
            Id = id;
            Address = address;
            Status = SwimMemberStatus.Active;
            Incarnation = 0;
            Joined = LastSeen = DateTime.UtcNow;
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
        public int UdpPort { get; set; } = 1002;

        /// <summary>
        /// UDP multicast group on which new joining members will announce themselves.
        /// </summary>
        public string UdpMulticastGroupAddress { get; set; } = "FF01::1";
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
