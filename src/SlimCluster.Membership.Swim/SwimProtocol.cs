namespace SlimCluster.Membership.Swim
{
    using System;
    using System.Collections.Generic;

    public class SwimProtocol : IClusterMembership
    {
        /// <summary>
        /// The time period (T) at which the failure detection happen to every sub group of nodes (k).
        /// </summary>
        public TimeSpan Period { get; }

        /// <summary>
        /// The sub-group size for the failure detection (k). This indicates how many nodes are pinged in every T cycle.
        /// </summary>
        public int SubgroupSize { get; }

        public string ClusterId { get; }

        public IReadOnlyCollection<IMember> Members => throw new NotImplementedException();

        public event IClusterMembership.MemberJoinedEventHandler? MemberJoined;
        public event IClusterMembership.MemberLeftEventHandler? MemberLeft;

        public SwimProtocol(string clusterId, TimeSpan period)
        {
            ClusterId = clusterId;
            Period = period;
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

        public static readonly SwimMemberStatus Active = new SwimMemberStatus(new Guid(""), "Active");
        public static readonly SwimMemberStatus Suspicious = new SwimMemberStatus(new Guid(""), "Suspicious");
        public static readonly SwimMemberStatus Confirm = new SwimMemberStatus(new Guid(""), "Confirm");
    }
}
