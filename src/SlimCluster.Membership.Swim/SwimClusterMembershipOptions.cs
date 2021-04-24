namespace SlimCluster.Membership.Swim
{
    using System;
    using System.Net.Sockets;

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

        public AddressFamily AddressFamily { get; set; } = AddressFamily.InterNetwork;
    }

}
