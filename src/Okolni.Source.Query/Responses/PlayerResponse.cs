﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Okolni.Source.Query.Responses
{
    public class PlayerResponse : IResponse
    {
        /// <summary>
        /// Always equal to 'D' (0x44)
        /// </summary>
        public byte Header { get; set; }

        /// <summary>
        /// Amount of retries it took to get the result
        /// </summary>
        public int Retries { get; set; }

        /// <summary>
        /// List of Players on the server
        /// </summary>
        public List<Player> Players { get; set; }
    }

    public class Player
    {
        /// <summary>
        /// Index of player chunk starting from 0.
        /// </summary>
        public byte Index { get; set; }

        /// <summary>
        /// Name of the player.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Player's score (usually "frags" or "kills".)
        /// </summary>
        public uint Score { get; set; }

        /// <summary>
        /// Time (in seconds) player has been connected to the server.
        /// </summary>
        public TimeSpan Duration { get; set; }
    }
}