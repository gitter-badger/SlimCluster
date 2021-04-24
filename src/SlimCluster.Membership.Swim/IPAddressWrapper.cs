namespace SlimCluster.Membership.Swim
{
    using System.Net;

    public class IPAddressWrapper : IAddress
    {
        public IPEndPoint EndPoint { get; }

        public IPAddressWrapper(IPEndPoint endPoint)
        {
            EndPoint = endPoint;
        }
    }

}
