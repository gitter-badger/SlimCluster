namespace SlimCluster.Membership.Swim
{
    using System;

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

}
