using System;

namespace CCTV.Models
{
    public class CCTVSettings
    {
        public string Name { get; set; }
        public string IP { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public ushort Port { get; set; }

        public CCTVSettings(string name, string ip, string username, string password, ushort port)
        {
            Name = name;
            IP = ip;
            Username = username;
            Password = password;
            Port = port;
        }

        public override string ToString()
        {
            return Name;
        }
    }
} 