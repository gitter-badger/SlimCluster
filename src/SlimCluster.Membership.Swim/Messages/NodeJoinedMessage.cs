namespace SlimCluster.Membership.Swim.Messages
{
    using Newtonsoft.Json;

    public class NodeJoinedMessage
    {
        [JsonProperty("nid")]
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

}
