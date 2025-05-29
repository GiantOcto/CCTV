using System;
using System.Runtime.InteropServices;

namespace CCTV.Models
{
    public class CCTV
    {
        public const string HCNET_SDK = "HCNetSDK.dll";
        public const string HCCORE_SDK = "HCCore.dll";
        public const string PLAYCTRL_SDK = "PlayCtrl.dll";

        // 알람 관련
        public const int COMM_ALARM_RULE = 0x1102;  // 지능형 분석 알람
        public const int COMM_ALARM_MOTION = 0x4000;  // 일반 움직임 감지 알람
        public const int COMM_MOTION_ALARM = 0x4010;  // 다른 형태의 움직임 감지 알람
        public const int MAX_NAMELEN = 16;  // 이름 최대 길이

        // HikVision SDK 검색 반환 상수값
        public const int NET_DVR_FILE_SUCCESS = 1000;    // 파일 찾기 성공
        public const int NET_DVR_FILE_NOFIND = 1001;     // 파일 찾기 실패
        public const int NET_DVR_ISFINDING = 1002;       // 파일 찾기 중
        public const int NET_DVR_NOMOREFILE = 1003;      // 더 이상 파일이 없음
        public const int NET_DVR_FILE_EXCEPTION = 1004;  // 검색 예외 상황

        // FindNextFile 상수 추가
        public enum ENUM_NEXTFIND : uint
        {
            NET_SDK_FIND_NEXT_STATUS = 0,    // 상태 반환
            NET_SDK_FIND_NEXT_PRESENCE = 1,  // 존재 여부 반환
            NET_SDK_FIND_NEXT_EXITFIND = 2,  // 검색 종료
            NET_SDK_FIND_NEXT_GETDATA = 3,   // 데이터 반환
            NET_SDK_FIND_NEXT_GETFACEPICINFODATA = 4 // 얼굴 정보 데이터 반환
        }

        // PTZ 제어 명령 상수 확인 및 수정
        public const int PTZ_PAN_LEFT = 23;      // 좌측 이동 
        public const int PTZ_PAN_RIGHT = 24;     // 우측 이동 
        public const int PTZ_TILT_UP = 21;       // 상향 이동 
        public const int PTZ_TILT_DOWN = 22;     // 하향 이동 
        public const int PTZ_ZOOM_IN = 11;       // 줌인 
        public const int PTZ_ZOOM_OUT = 12;      // 줌아웃
        public const int PTZ_FOCUS_NEAR = 13;    // 포커스 가깝게 
        public const int PTZ_FOCUS_FAR = 14;     // 포커스 멀게 
        public const int PTZ_STOP = 5;           // 정지

        // HCCore.dll 함수들
        [DllImport(HCCORE_SDK)]
        public static extern bool HCCORE_Init();

        [DllImport(HCCORE_SDK)]
        public static extern bool HCCORE_Cleanup();

        // PlayCtrl.dll 함수들
        [DllImport(PLAYCTRL_SDK)]
        public static extern bool PlayM4_GetPort(ref uint nPort);

        [DllImport(PLAYCTRL_SDK)]
        public static extern bool PlayM4_FreePort(uint nPort);

        // 기본 SDK 함수
        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_Init();

        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_Cleanup();

        [DllImport(HCNET_SDK)]
        public static extern uint NET_DVR_GetLastError();

        // 로그 설정 함수 추가
        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_SetLogToFile(int nLogLevel, string strLogDir, bool bAutoDel);

        // 로그인/로그아웃 관련
        [DllImport(HCNET_SDK)]
        public static extern int NET_DVR_Login_V30(string sDVRIP, ushort wDVRPort, string sUserName, string sPassword, ref NET_DVR_DEVICEINFO_V30 lpDeviceInfo);

        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_Logout(int lUserID);

        // 실시간 재생 함수 추가
        [DllImport(HCNET_SDK)]
        public static extern int NET_DVR_RealPlay_V40(int lUserID, ref NET_DVR_PREVIEWINFO lpPreviewInfo, 
            NET_DVR_REALPLAY_CALLBACK fRealDataCallBack, IntPtr pUser);

        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_StopRealPlay(int lRealPlayHandle);

        // 알람 관련 함수 추가 (기존 클래스에 추가)
        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_SetDVRMessageCallBack_V31(MSGCallBackV31 fMessageCallBack, IntPtr pUser);

        [DllImport(HCNET_SDK)]
        public static extern int NET_DVR_SetupAlarmChan_V41(int lUserID, ref NET_DVR_SETUPALARM_PARAM lpSetupParam);

        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_CloseAlarmChan_V30(int lAlarmHandle);

        // 콜백 델리게이트 추가
        public delegate bool MSGCallBackV31(int lCommand, ref NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser);

        // 알람 관련 구조체 추가
        [StructLayout(LayoutKind.Sequential)]
        public struct NET_DVR_SETUPALARM_PARAM
        {
            public uint dwSize;
            public byte byLevel;             // 알람 우선순위: 0-높음, 1-중간, 2-낮음
            public byte byAlarmInfoType;     // 알람 정보 유형: 0-이진, 1-텍스트
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 14)]
            public byte[] byRes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NET_DVR_ALARMER
        {
            public uint dwSize;
            public int lUserID;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
            public byte[] sDeviceIP;
            public int lDeviceIndex;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] byRes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NET_VCA_RULE_INFO
        {
            public byte byRuleID;
            public byte byRes;
            public ushort wEventTypeEx;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_NAMELEN)]
            public byte[] byRuleName;
            public uint dwEventType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] byRes1;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NET_VCA_RULE_ALARM
        {
            public uint dwSize;
            public uint dwRelativeTime;
            public uint dwAbsTime;
            public NET_VCA_RULE_INFO struRuleInfo;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)]
            public byte[] byRes;
            public uint dwPicDataLen;
            public IntPtr pImage;
        }

        // PTZ 제어 함수 - 속도 조절 가능
        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_PTZControlWithSpeed_Other(
            int lUserID,         // 로그인 핸들
            int lChannel,        // 채널 번호
            uint dwPTZCommand,   // PTZ 명령 (위에 정의한 상수)
            uint dwStop,         // 0=명령 시작, 1=명령 종료
            uint dwSpeed);       // 속도 (1-7, 값이 클수록 빠름)

        // PTZ 제어 함수 - 기본 속도
        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_PTZControl_Other(
            int lUserID,         // 로그인 핸들
            int lChannel,        // 채널 번호
            uint dwPTZCommand,   // PTZ 명령 (위에 정의한 상수)
            uint dwStop);        // 0=명령 시작, 1=명령 종료

        // 프리셋 관련 상수 및 함수
        public const uint SET_PRESET = 8;     // 프리셋 설정
        public const uint CLE_PRESET = 9;     // 프리셋 삭제
        public const uint GOTO_PRESET = 39;   // 프리셋 이동

        // 프리셋 위치 설정/호출
        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_PTZPreset_Other(
            int lUserID,         // 로그인 핸들
            int lChannel,        // 채널 번호
            uint dwPTZPresetCmd, // 프리셋 명령 (SET_PRESET, GOTO_PRESET 등)
            uint dwPresetIndex); // 프리셋 번호 (1-255)


        // 콜백 델리게이트 정의
        public delegate void NET_DVR_REALPLAY_CALLBACK(int lRealHandle, uint dwDataType, IntPtr pBuffer, uint dwBufSize, IntPtr pUser);

        // 구조체 정의
        [StructLayoutAttribute(LayoutKind.Sequential)]
        public struct NET_DVR_DEVICEINFO_V30
        {
            [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 48)]
            public byte[] sSerialNumber;
            public byte byAlarmInPortNum;
            public byte byAlarmOutPortNum;
            public byte byDiskNum;
            public byte byDVRType;
            public byte byChanNum;
            public byte byStartChan;
            public byte byAudioChanNum;
            public byte byIPChanNum;
            public byte byZeroChanNum;
            public byte byMainProto;
            public byte bySubProto;
            public byte bySupport;
            public byte bySupport1;
            public byte bySupport2;
            public ushort wDevType;
            [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 24)]
            public byte[] byRes1;
        }

        [StructLayoutAttribute(LayoutKind.Sequential)]
        public struct NET_DVR_PREVIEWINFO
        {
            public int lChannel;
            public uint dwStreamType;
            public uint dwLinkMode;
            public IntPtr hPlayWnd;
            public bool bBlocked;
            public bool bPassbackRecord;
            public byte byPreviewMode;
            public byte byStreamID;
            public byte byProtoType;
            public byte byRes1;
            public byte byVideoCodingType;
            [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 31)]
            public byte[] byRes;
        }

        // 녹화 영상 재생 관련 상수 및 구조체 추가
        public const int NET_DVR_PLAYSTART = 1;    // 재생 시작
        public const int NET_DVR_PLAYSTOP = 2;     // 재생 중지
        public const int NET_DVR_PLAYPAUSE = 3;    // 일시 정지
        public const int NET_DVR_PLAYRESTART = 4;  // 재생 재시작
        public const int NET_DVR_PLAYFAST = 5;     // 빨리 감기
        public const int NET_DVR_PLAYSLOW = 6;     // 느리게 재생
        public const int NET_DVR_PLAYFRAME = 7;    // 프레임 단위 재생
        public const int NET_DVR_PLAYNORMAL = 8;   // 정상 속도 재생
        public const int NET_DVR_PLAYGETPOS = 13;  // 재생 위치 가져오기
        public const int NET_DVR_PLAYSETPOS = 14;  // 재생 위치 설정하기

        // 녹화 영상 재생 관련 콜백 델리게이트
        public delegate void NET_DVR_PLAYBACK_CALLBACK(int lPlayHandle, uint dwDataType, IntPtr pBuffer, uint dwBufSize, IntPtr pUser);

        // 녹화 영상 재생 관련 구조체
        [StructLayout(LayoutKind.Sequential)]
        public struct NET_DVR_TIME
        {
            public uint dwYear;
            public uint dwMonth;
            public uint dwDay;
            public uint dwHour;
            public uint dwMinute;
            public uint dwSecond;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NET_DVR_VOD_PARA
        {
            public uint dwSize;
            public NET_DVR_TIME struBeginTime;
            public NET_DVR_TIME struEndTime;
            public IntPtr hWnd;
            public byte byDrawFrame;
            public byte byVolumeType;
            public byte byVolumeNum;
            public byte byStreamType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
            public byte[] byRes;
        }

        // 녹화 영상 재생 함수
        [DllImport(HCNET_SDK)]
        public static extern int NET_DVR_PlayBackByTime_V40(int lUserID, ref NET_DVR_VOD_PARA struVodPara);

        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_PlayBackControl_V40(int lPlayHandle, uint dwControlCode, IntPtr lpInBuffer, uint dwInLen, IntPtr lpOutBuffer, ref uint lpOutLen);

        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_StopPlayBack(int lPlayHandle);

        // 재생 진행 상태 가져오기
        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_PlayBackControl(int lPlayHandle, uint dwControlCode, uint dwInValue, ref uint lpOutValue);

        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_GetPlayBackOsdTime(int lPlayHandle, ref NET_DVR_TIME lpOsdTime);

        // 녹화 파일 검색 관련 구조체 및 함수
        [StructLayout(LayoutKind.Sequential)]
        public struct NET_DVR_FILECOND_V40
        {
            public int lChannel;
            public uint dwFileType;
            public uint dwIsLocked;
            public uint dwUseCardNo;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] sCardNumber;
            public NET_DVR_TIME struStartTime;
            public NET_DVR_TIME struStopTime;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] byRes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NET_DVR_FINDDATA_V40
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
            public byte[] sFileName;
            public NET_DVR_TIME struStartTime;
            public NET_DVR_TIME struStopTime;
            public uint dwFileSize;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] sCardNum;
            public byte byLocked;
            public byte byFileType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)]
            public byte[] byRes;
        }

        [DllImport(HCNET_SDK)]
        public static extern int NET_DVR_FindFile_V40(int lUserID, ref NET_DVR_FILECOND_V40 pFindCond);

        [DllImport(HCNET_SDK)]
        public static extern int NET_DVR_FindNextFile_V40(int lFindHandle, ref NET_DVR_FINDDATA_V40 lpFindData);

        [DllImport(HCNET_SDK)]
        public static extern bool NET_DVR_FindClose_V30(int lFindHandle);

        // 채널 번호 기반 재생 함수 추가
        [StructLayout(LayoutKind.Sequential)]
        public struct NET_DVR_TIME_SEARCH
        {
            public uint dwChannel;  // 채널 번호
            public NET_DVR_TIME struStartTime;
            public NET_DVR_TIME struStopTime;
            public IntPtr hWnd;  // 재생 창 핸들
            public byte byDrawFrame;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 63)]
            public byte[] byRes;
        }

        [DllImport(HCNET_SDK)]
        public static extern int NET_DVR_PlayBackByTime(int lUserID, int lChannel, ref NET_DVR_TIME lpStartTime, ref NET_DVR_TIME lpStopTime, IntPtr hWnd);
    }
}