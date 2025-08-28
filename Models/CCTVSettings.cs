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
        
        // 채널 관련 속성 추가
        public int DefaultChannel { get; set; }
        public int MinChannel { get; set; }
        public int MaxChannel { get; set; }

        public CCTVSettings(string name, string ip, string username, string password, ushort port)
        {
            Name = name;
            IP = ip;
            Username = username;
            Password = password;
            Port = port;
            
            // 기본값 설정 (기존과 동일하게 33-40)
            DefaultChannel = 33;
            MinChannel = 33;
            MaxChannel = 40;
        }
        
        // 채널 범위를 지정하는 생성자 추가
        public CCTVSettings(string name, string ip, string username, string password, ushort port, int defaultChannel, int minChannel, int maxChannel)
        {
            Name = name;
            IP = ip;
            Username = username;
            Password = password;
            Port = port;
            DefaultChannel = defaultChannel;
            MinChannel = minChannel;
            MaxChannel = maxChannel;
        }

        public override string ToString()
        {
            return Name;
        }
    }
} 