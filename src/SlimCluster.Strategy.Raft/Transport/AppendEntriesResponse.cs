﻿namespace SlimCluster.Strategy.Raft
{
    public class AppendEntriesResponse
    {
        public bool Success { get; set; }
        public int Term { get; set; }
    }
}
