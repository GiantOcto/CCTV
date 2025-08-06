using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CCTV.Models;
using System.IO;
using System.Runtime.InteropServices;
using CCTV.Views;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;

namespace CCTV.ViewModels
{
    public partial class CCTVViewModel : ObservableObject, IDisposable
    {
        // 하이크비전 SDK 관련 필드
        private int m_lUserID = -1;
        private Dictionary<int, int> m_lRealHandles = new Dictionary<int, int>();
        private bool m_bInitSDK = false;
        
        // 상태 메시지
        [ObservableProperty]
        private string statusMessage = "준비됨";
        
        // 연결 상태
        [ObservableProperty]
        private bool isConnected = false;
        
        // 표시할 채널 번호 (기본값 설정)
        private int channelNumber = 33;
        
        // 비디오 이미지 컨트롤 참조
        private Image videoControl;

        private static Models.CCTV.MSGCallBackV31 m_fMSGCallBack;
        private int m_lAlarmHandle = -1;
        
        // 추가할 필드
        private bool _isIntrusionActive = false;
        private string _activeIntrusionImagePath = null;
        private DispatcherTimer _intrusionCheckTimer = null;
        private DateTime _lastIntrusionTime = DateTime.MinValue;
        private readonly TimeSpan _intrusionTimeout = TimeSpan.FromSeconds(3);

         // 파일 접근을 위한 동기화 객체 추가
        private static readonly object _logLock = new object();

         public ICommand PanLeftCommand { get; }
        public ICommand PanRightCommand { get; }
        public ICommand TiltUpCommand { get; }
        public ICommand TiltDownCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand GoToPreset1Command { get; }
        public ICommand GoToPreset2Command { get; }
        
        public CCTVViewModel(Image videoControl)
        {
            this.videoControl = videoControl;
         
            InitSDK();

              // 알람 콜백 설정
            SetupAlarmCallback();

            PanLeftCommand = new RelayCommand(PanLeft);
            PanRightCommand = new RelayCommand(PanRight);
            TiltUpCommand = new RelayCommand(TiltUp);
            TiltDownCommand = new RelayCommand(TiltDown);
            ZoomInCommand = new RelayCommand(ZoomIn);
            ZoomOutCommand = new RelayCommand(ZoomOut);
            GoToPreset1Command = new RelayCommand(() => GoToPreset(1));
            GoToPreset2Command = new RelayCommand(() => GoToPreset(2));
        }
        
        private void InitSDK()
        {
            try
            {
                // 로그 디렉토리 설정
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SDKLog");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                
                // SDK 초기화
                m_bInitSDK = Models.CCTV.NET_DVR_Init();
                if (!m_bInitSDK)
                {
                    StatusMessage = "SDK 초기화 실패";
                    return;
                }
                
                // 로그 설정
                Models.CCTV.NET_DVR_SetLogToFile(3, logDir, true);
                StatusMessage = "SDK 초기화 완료";
            }
            catch (Exception ex)
            {
                StatusMessage = $"SDK 초기화 오류: {ex.Message}";
            }
        }
        
        [RelayCommand]
        private async Task Connect()
        {
            try
            {
                // UI 상태 즉시 업데이트
                StatusMessage = "연결 중...";
                
                // 이미 연결된 경우 먼저 해제
                if (m_lUserID >= 0)
                {
                    await Task.Run(() => Disconnect());
                }

                // 연결 작업을 백그라운드에서 실행
                await Task.Run(() =>
                {
                    string ip = "49.1.131.113";
                    string username = "admin";
                    string password = "!yanry4880";
                    ushort port = 9000;

                    // 로그인 구조체 설정
                    Models.CCTV.NET_DVR_DEVICEINFO_V30 deviceInfo = new Models.CCTV.NET_DVR_DEVICEINFO_V30();
                    
                    // 로그인
                    m_lUserID = Models.CCTV.NET_DVR_Login_V30(ip, port, username, password, ref deviceInfo);
                    if (m_lUserID < 0)
                    {
                        uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                        
                        // UI 스레드에서 상태 메시지 업데이트
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"로그인 실패: 오류 코드 {errorCode}";
                            IsConnected = false;
                        });
                        return;
                    }
                    
                    // UI 스레드에서 성공 상태 업데이트
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = "NVR 로그인 성공";
                        IsConnected = true;
                    });
                });
                
                // 연결 성공 시 추가 작업
                if (IsConnected)
                {
                    // 실시간 비디오 스트림 시작 (백그라운드에서)
                    await Task.Run(() => StartRealPlay());
                    
                    // 알람 채널 설정 (백그라운드에서)
                    await Task.Run(() =>
                    {
                        SetupAlarmChan();
                        LogMessage($"알람 채널 설정 - 채널: {channelNumber}");
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"연결 오류: {ex.Message}";
                LogError($"비동기 연결 오류: {ex.Message}");
            }
        }
        
        private void StartRealPlay()
        {
            try
            {
                if (m_lUserID < 0)
                {
                    StatusMessage = "먼저 NVR에 로그인하세요";
                    return;
                }
                
                // 1단계: 모든 기존 스트림 완전히 중지
                StopAllStreams();
                
                // 2단계: SDK 내부 정리를 위한 최소 지연
                Thread.Sleep(100);
                
                // 3단계: 윈도우 핸들 가져오기
                IntPtr hwnd = GetVideoWindowHandle(videoControl);
                if (hwnd == IntPtr.Zero)
                {
                    StatusMessage = "비디오 윈도우 핸들을 가져올 수 없음";
                    return;
                }
                
                // 4단계: 실시간 재생 구조체 설정 (빠른 전환을 위해 최적화)
                Models.CCTV.NET_DVR_PREVIEWINFO lpPreviewInfo = new Models.CCTV.NET_DVR_PREVIEWINFO
                {
                    lChannel = channelNumber,  // 현재 채널 사용
                    dwStreamType = 0,          // 메인 스트림
                    dwLinkMode = 0,            // TCP 모드
                    hPlayWnd = hwnd,           // 비디오 윈도우 핸들
                    bBlocked = false           // 논블로킹 모드 (빠른 전환)
                };
                
                // 5단계: 실시간 재생 시작
                int realHandle = Models.CCTV.NET_DVR_RealPlay_V40(m_lUserID, ref lpPreviewInfo, null, IntPtr.Zero);
                
                if (realHandle < 0)
                {
                    uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                    StatusMessage = $"채널 {channelNumber} 실시간 재생 시작 실패: 오류 코드 {errorCode}";
                    LogError($"재생 실패 - 채널: {channelNumber}, 에러: {errorCode}");
                    return;
                }
                
                // 6단계: 재생 핸들 저장
                m_lRealHandles[channelNumber] = realHandle;
                StatusMessage = $"채널 {channelNumber} 영상 스트리밍 시작";
                LogMessage($"채널 {channelNumber} 스트림 시작 성공: 핸들 {realHandle}");
                
            }
            catch (Exception ex)
            {
                StatusMessage = $"채널 {channelNumber} 비디오 스트림 시작 오류: {ex.Message}";
                LogError($"StartRealPlay 예외: {ex.Message}");
            }
        }

        private IntPtr GetVideoWindowHandle(Image control)
        {
            if (control == null)
                return IntPtr.Zero;

            IntPtr hwnd = IntPtr.Zero;

            try
            {
                // UI 스레드에서 실행해야 함
                control.Dispatcher.Invoke(() =>
                {
                    // 컨트롤이 로드될 때까지 대기
                    if (!control.IsLoaded)
                    {
                        File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cctv_debug.txt"),
                            $"[{DateTime.Now}] 컨트롤이 아직 로드되지 않음\r\n");
                    }

                    // 컨트롤이 렌더링될 때까지 대기
                    control.UpdateLayout();

                    // 컨트롤이 렌더링 트리에 추가되었는지 확인
                    PresentationSource source = PresentationSource.FromVisual(control);
                    if (source == null)
                    {
                        File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cctv_debug.txt"),
                            $"[{DateTime.Now}] 컨트롤이 렌더링 트리에 없음\r\n");

                        // 부모 윈도우 핸들 사용
                        Window window = Window.GetWindow(control);
                        if (window != null)
                        {
                            System.Windows.Interop.WindowInteropHelper helper =
                                new System.Windows.Interop.WindowInteropHelper(window);
                            hwnd = helper.Handle;

                            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cctv_debug.txt"),
                                $"[{DateTime.Now}] 윈도우 핸들 사용: {hwnd}\r\n");
                        }
                        return;
                    }

                    System.Windows.Interop.HwndSource hwndSource =
                        source as System.Windows.Interop.HwndSource;

                    if (hwndSource != null)
                    {
                        hwnd = hwndSource.Handle;
                        File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cctv_debug.txt"),
                            $"[{DateTime.Now}] 컨트롤 핸들 얻음: {hwnd}\r\n");
                    }
                });

                if (hwnd == IntPtr.Zero)
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cctv_debug.txt"),
                        $"[{DateTime.Now}] 핸들을 얻지 못함, 윈도우 핸들 시도\r\n");

                    // 대체 방법: Window.Handle 직접 사용
                    control.Dispatcher.Invoke(() => {
                        var mainWindow = Application.Current.MainWindow;
                        if (mainWindow != null)
                        {
                            var helper = new System.Windows.Interop.WindowInteropHelper(mainWindow);
                            hwnd = helper.Handle;
                            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cctv_debug.txt"),
                                $"[{DateTime.Now}] 메인 윈도우 핸들 사용: {hwnd}\r\n");
                        }
                    });
                }

                return hwnd;
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cctv_debug.txt"),
                    $"[{DateTime.Now}] 핸들 가져오기 실패 - {ex.Message}\r\n{ex.StackTrace}\r\n");
                return IntPtr.Zero;
            }
        }

        [RelayCommand]
        private void Disconnect()
        {
            try
            {
                // 모든 실시간 재생 중지
                foreach (var handle in m_lRealHandles)
                {
                    if (handle.Value >= 0)
                    {
                        Models.CCTV.NET_DVR_StopRealPlay(handle.Value);
                    }
                }
                m_lRealHandles.Clear();
                
                // 로그아웃
                if (m_lUserID >= 0)
                {
                    Models.CCTV.NET_DVR_Logout(m_lUserID);
                    m_lUserID = -1;
                }
                
                IsConnected = false;
                StatusMessage = "연결 종료됨";
            }
            catch (Exception ex)
            {
                StatusMessage = $"연결 종료 오류: {ex.Message}";
            }
        }
        
        // 채널 변경 메서드
        public void ChangeChannel(int newChannelNumber)
        {
            try
            {
                int oldChannel = channelNumber;
                LogMessage($"채널 변경 요청: {oldChannel} → {newChannelNumber}");
                
                // 이미 같은 채널이면 무시
                if (channelNumber == newChannelNumber)
                {
                    StatusMessage = $"이미 채널 {channelNumber}에 연결되어 있습니다";
                    LogMessage($"채널 변경 무시: 이미 채널 {channelNumber}");
                    return;
                }
                
                // 연결되지 않은 상태에서는 채널 번호만 변경
                if (!IsConnected)
                {
                    channelNumber = newChannelNumber;
                    StatusMessage = $"채널 {channelNumber}로 설정됨 (연결 후 적용)";
                    LogMessage($"연결 전 채널 설정: {newChannelNumber}");
                    return;
                }
                
                StatusMessage = $"채널 {newChannelNumber}로 변경 중...";
                
                // 1단계: 모든 스트림 완전히 중지
                StopAllStreams();
                
                // 2단계: 채널 번호 변경
                channelNumber = newChannelNumber;
                
                // 3단계: 최소한의 지연 (SDK 내부 정리 시간)
                Thread.Sleep(200);
                
                // 4단계: 새 채널로 스트림 시작
                StartRealPlay();
                
                LogMessage($"채널 변경 완료: {oldChannel} → {newChannelNumber}");
                
            }
            catch (Exception ex)
            {
                StatusMessage = $"채널 변경 오류: {ex.Message}";
                LogError($"채널 변경 오류: {ex.Message}");
            }
        }
        
        // 현재 채널 번호를 반환하는 공개 프로퍼티
        public int ChannelNumber 
        { 
            get => channelNumber; 
            set 
            { 
                if (channelNumber != value)
                {
                    ChangeChannel(value);
                }
            } 
        }
        
        public void Dispose()
        {
            try
            {
                LogMessage("CCTVViewModel Dispose 시작");
                
                // 인트루전 체크 타이머 정리
                if (_intrusionCheckTimer != null)
                {
                    _intrusionCheckTimer.Stop();
                    _intrusionCheckTimer.Tick -= IntrusionCheckTimer_Tick;
                    _intrusionCheckTimer = null;
                    LogMessage("인트루전 체크 타이머 정리 완료");
                }
                
                // 알람 채널 해제
                if (m_lAlarmHandle >= 0)
                {
                    Models.CCTV.NET_DVR_CloseAlarmChan_V30(m_lAlarmHandle);
                    m_lAlarmHandle = -1;
                    LogMessage("알람 채널 해제 완료");
                }
                
                // 모든 스트림 중지
                StopAllStreams();
                
                // 로그아웃
                if (m_lUserID >= 0)
                {
                    Models.CCTV.NET_DVR_Logout(m_lUserID);
                    m_lUserID = -1;
                    LogMessage("NVR 로그아웃 완료");
                }
                
                // SDK 정리
                if (m_bInitSDK)
                {
                    Models.CCTV.NET_DVR_Cleanup();
                    m_bInitSDK = false;
                    LogMessage("SDK 정리 완료");
                }
                
                LogMessage("CCTVViewModel Dispose 완료");
            }
            catch (Exception ex)
            {
                LogError($"CCTVViewModel Dispose 중 오류: {ex.Message}");
            }
        }

        // 새로운 메서드 추가 - Window가 로드된 후 호출됨
        public void OnWindowLoaded()
        {
            // 초기화만 하고 자동 연결은 하지 않음
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cctv_debug.txt"),
                $"[{DateTime.Now}] OnWindowLoaded 호출됨 - 자동 연결 없음\r\n");

            // 핸들 확인만 수행
            IntPtr hwnd = GetVideoWindowHandle(videoControl);
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cctv_debug.txt"),
                $"[{DateTime.Now}] 윈도우 로드 직후 핸들: {hwnd}\r\n");
                
            // 자동 연결 제거 - 사용자가 채널 버튼을 클릭할 때만 연결
        }

         private void SetupAlarmCallback()
        {
            // 콜백 함수 등록
            m_fMSGCallBack = new Models.CCTV.MSGCallBackV31(MessageCallback);
            Models.CCTV.NET_DVR_SetDVRMessageCallBack_V31(m_fMSGCallBack, IntPtr.Zero);
        }
        
        // 로그인 성공 후 호출
        private void SetupAlarmChan()
        {
            if (m_lUserID < 0)
            {
                LogError("알람 채널 설정 실패: 로그인되지 않음");
                return;
            }
                
            // 알람 파라미터 설정
            Models.CCTV.NET_DVR_SETUPALARM_PARAM struAlarmParam = new Models.CCTV.NET_DVR_SETUPALARM_PARAM();
            struAlarmParam.dwSize = (uint)Marshal.SizeOf(struAlarmParam);
            struAlarmParam.byLevel = 1; // 중간 우선순위
            struAlarmParam.byAlarmInfoType = 1; // 텍스트 알람 정보
            
            // 알람 채널 시작
            m_lAlarmHandle = Models.CCTV.NET_DVR_SetupAlarmChan_V41(m_lUserID, ref struAlarmParam);
            
            if (m_lAlarmHandle < 0)
            {
                LogError($"알람 채널 설정 실패: {Models.CCTV.NET_DVR_GetLastError()}");
            }
            else
            {
                LogMessage("알람 채널 설정 성공");
            }
        }

        // 알람 메시지 콜백 함수
        private bool MessageCallback(int lCommand, ref Models.CCTV.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen, IntPtr pUser)
        {
            try
            {
                // 디버그 정보 추가
                LogMessage($"알람 콜백 발생: 명령={lCommand:X}, 장치인덱스={pAlarmer.lDeviceIndex}");
                
                // 지능형 분석 알람인 경우
                if (lCommand == Models.CCTV.COMM_ALARM_RULE)
                {
                    LogMessage("지능형 분석 알람 감지됨");
                    ProcessIntrusionAlarm(pAlarmer, pAlarmInfo, dwBufLen);
                }
                // 모션 감지 알람 처리 - 상수 사용
                else if (lCommand == Models.CCTV.COMM_ALARM_MOTION || lCommand == Models.CCTV.COMM_MOTION_ALARM)
                {
                    LogMessage($"움직임 감지 알람 감지됨: {lCommand:X}");
                    ProcessMotionAlarm(pAlarmer);
                }
                else
                {
                    LogMessage($"다른 종류의 알람: {lCommand:X} - 처리되지 않음");
                }
            }
            catch (Exception ex)
            {
                LogError($"알람 콜백 오류: {ex.Message}");
            }
            
            return true;
        }
        
        // 일반 움직임 감지 알람 처리 (새 메서드 추가)
        private void ProcessMotionAlarm(Models.CCTV.NET_DVR_ALARMER pAlarmer)
        {
            try
            {
                LogMessage($"움직임 감지 알람 처리 - 장치인덱스: {pAlarmer.lDeviceIndex}, 현재채널: {channelNumber}");
                
                // 감지 상태 활성화
                SetIntrusionActive();
                
                // UI 스레드에서 알림 창 표시
                Application.Current.Dispatcher.Invoke(() => {
                    StatusMessage = $"움직임 감지됨 - 채널: {channelNumber}";
                    LogMessage($"움직임 감지 상태 메시지 업데이트 - 채널: {channelNumber}");
                });
            }
            catch (Exception ex)
            {
                LogError($"움직임 알람 처리 오류: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        // 침입 알람 처리
        private void ProcessIntrusionAlarm(Models.CCTV.NET_DVR_ALARMER pAlarmer, IntPtr pAlarmInfo, uint dwBufLen)
        {
            try
            {
                // 구조체로 변환
                Models.CCTV.NET_VCA_RULE_ALARM struRuleAlarm = (Models.CCTV.NET_VCA_RULE_ALARM)Marshal.PtrToStructure(
                    pAlarmInfo, typeof(Models.CCTV.NET_VCA_RULE_ALARM));
                
                LogMessage($"이벤트 타입: {struRuleAlarm.struRuleInfo.dwEventType}, 확장타입: {struRuleAlarm.struRuleInfo.wEventTypeEx}, 장치인덱스: {pAlarmer.lDeviceIndex}, 현재채널: {channelNumber}");
                
                // 모든 이벤트 타입 처리 (수정)
                bool processEvent = true;
                string alertMessage = "움직임이 감지되었습니다!";
                
                // 타입에 따른 메시지 변경 (옵션)
                if (struRuleAlarm.struRuleInfo.wEventTypeEx == 1)
                {
                    alertMessage = "움직임이 감지되었습니다!";
                    LogMessage($"움직임 감지 - 장치인덱스: {pAlarmer.lDeviceIndex}, 현재채널: {channelNumber}");
                }
                else if (struRuleAlarm.struRuleInfo.wEventTypeEx == 2)
                {
                    alertMessage = "구역에 진입했습니다!";
                    LogMessage($"구역 진입 감지 - 장치인덱스: {pAlarmer.lDeviceIndex}, 현재채널: {channelNumber}");
                }
                else
                {
                    // 기타 다른 이벤트 타입도 처리
                    LogMessage($"기타 이벤트 타입 감지: {struRuleAlarm.struRuleInfo.wEventTypeEx} - 처리함");
                }
                
                if (processEvent)
                {
                    // 이미지 저장 (필요 시)
                    string imagePath = SaveDetectionImage(struRuleAlarm);
                    
                    // 감지 상태 활성화
                    SetIntrusionActive();
                    
                    // UI 스레드에서 상태 메시지 업데이트
                    Application.Current.Dispatcher.Invoke(() => {
                        StatusMessage = $"침입 감지됨 - 채널: {channelNumber}";
                        LogMessage($"침입 감지 상태 메시지 업데이트 - 채널: {channelNumber}, 메시지: {alertMessage}");
                    });
                }
            }
            catch (Exception ex)
            {
                LogError($"알람 처리 오류: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        // 침입 감지 상태 변경 및 확인 메서드 추가
        private void SetIntrusionActive()
        {
            _isIntrusionActive = true;
            _lastIntrusionTime = DateTime.Now;
            
            // 기존 타이머가 있으면 중지
            if (_intrusionCheckTimer != null)
            {
                _intrusionCheckTimer.Stop();
            }
            else
            {
                // 타이머 생성
                _intrusionCheckTimer = new DispatcherTimer();
                _intrusionCheckTimer.Interval = TimeSpan.FromSeconds(0.5); // 0.5초마다 체크
                _intrusionCheckTimer.Tick += IntrusionCheckTimer_Tick;
            }
            
            _intrusionCheckTimer.Start();
        }
        
        private void IntrusionCheckTimer_Tick(object sender, EventArgs e)
        {
            // 마지막 침입 시간으로부터 지정된 시간이 지났는지 확인
            if (DateTime.Now - _lastIntrusionTime > _intrusionTimeout)
            {
                _isIntrusionActive = false;
                _intrusionCheckTimer.Stop();
                LogMessage("침입 종료 감지 (시간 초과)");
            }
        }
        
        // 감지 이미지 저장
        private string SaveDetectionImage(Models.CCTV.NET_VCA_RULE_ALARM struRuleAlarm)
        {
            try
            {
                if (struRuleAlarm.dwPicDataLen > 0 && struRuleAlarm.pImage != IntPtr.Zero)
                {
                    string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IntrusionImages");
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    string filename = $"Intrusion_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                    string fullPath = Path.Combine(directory, filename);
                    
                    byte[] buffer = new byte[struRuleAlarm.dwPicDataLen];
                    Marshal.Copy(struRuleAlarm.pImage, buffer, 0, (int)struRuleAlarm.dwPicDataLen);
                    File.WriteAllBytes(fullPath, buffer);
                    
                    return fullPath;
                }
            }
            catch { }
            
            return null;
        }

        // 로그 메서드 수정
        private void LogMessage(string message)
        {
            Task.Run(() => {
                try
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cctv_log.txt");
                    lock (_logLock)
                    {
                        // 이미 파일이 크면 크기를 줄임
                        if (File.Exists(logPath) && new FileInfo(logPath).Length > 1024 * 1024 * 10) // 10MB 이상이면
                        {
                            try
                            {
                                File.WriteAllText(logPath, $"[{DateTime.Now}] 로그 파일이 너무 커서 초기화됨{Environment.NewLine}");
                            }
                            catch { /* 초기화 실패 무시 */ }
                        }
                        
                        File.AppendAllText(logPath, $"[{DateTime.Now}] {message}{Environment.NewLine}");
                    }
                }
                catch (Exception ex)
                {
                    // 로그 기록 실패를 콘솔에만 출력하고 무시
                    Console.WriteLine($"로그 기록 실패: {ex.Message}");
                }
            });
        }
        
        private void LogError(string message)
        {
            Task.Run(() => {
                try
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cctv_error.txt");
                    lock (_logLock)
                    {
                        File.AppendAllText(logPath, $"[{DateTime.Now}] {message}{Environment.NewLine}");
                    }
                }
                catch (Exception ex)
                {
                    // 로그 기록 실패를 콘솔에만 출력하고 무시
                    Console.WriteLine($"오류 로그 기록 실패: {ex.Message}");
                }
            });
        }

        public void PanLeft()
        {
            if (!IsConnected || m_lUserID < 0)
            {
                return;
            }

            try
            {
                uint command = 23; // DS-2CD2326G2 모델의 LEFT 명령
                
                Models.CCTV.NET_DVR_PTZControl_Other(m_lUserID, channelNumber, command, 0);
                System.Threading.Thread.Sleep(200);
                Models.CCTV.NET_DVR_PTZControl_Other(m_lUserID, channelNumber, command, 1);
            }
            catch (Exception ex)
            {
                LogError($"PanLeft 오류: {ex.Message}");
            }
        }

        public void PanRight()
        {
            if (!IsConnected || m_lUserID < 0)
            {
                return;
            }

            try
            {
                uint command = 24; // DS-2CD2326G2 모델의 RIGHT 명령
                
                Models.CCTV.NET_DVR_PTZControl_Other(m_lUserID, channelNumber, command, 0);
                System.Threading.Thread.Sleep(200);
                Models.CCTV.NET_DVR_PTZControl_Other(m_lUserID, channelNumber, command, 1);
            }
            catch (Exception ex)
            {
                LogError($"PanRight 오류: {ex.Message}");
            }
        }

        public void TiltUp()
        {
            if (!IsConnected || m_lUserID < 0)
            {
                return;
            }

            try
            {
                uint command = 21; // DS-2CD2326G2 모델의 UP 명령
                
                // 직접 제어 시도
                Models.CCTV.NET_DVR_PTZControl_Other(m_lUserID, channelNumber, command, 0);
                System.Threading.Thread.Sleep(200); // 명령 유지 시간 늘림
                Models.CCTV.NET_DVR_PTZControl_Other(m_lUserID, channelNumber, command, 1);
            }
            catch (Exception ex)
            {
                LogError($"TiltUp 오류: {ex.Message}");
            }
        }

        public void TiltDown()
        {
            if (!IsConnected || m_lUserID < 0)
            {
                return;
            }

            try
            {
                uint command = 22; // DS-2CD2326G2 모델의 DOWN 명령
                
                Models.CCTV.NET_DVR_PTZControl_Other(m_lUserID, channelNumber, command, 0);
                System.Threading.Thread.Sleep(200);
                Models.CCTV.NET_DVR_PTZControl_Other(m_lUserID, channelNumber, command, 1);
            }
            catch (Exception ex)
            {
                LogError($"TiltDown 오류: {ex.Message}");
            }
        }

        // 줌 관련 메서드
        public void ZoomIn()
        {
            if (!IsConnected || m_lUserID < 0)
            {
                return;
            }

            try
            {
                Models.CCTV.NET_DVR_PTZControl_Other(m_lUserID, channelNumber, (uint)Models.CCTV.PTZ_ZOOM_IN, 0);
                System.Threading.Thread.Sleep(200);
                Models.CCTV.NET_DVR_PTZControl_Other(m_lUserID, channelNumber, (uint)Models.CCTV.PTZ_ZOOM_IN, 1);
            }
            catch (Exception ex)
            {
                LogError($"ZoomIn 오류: {ex.Message}");
            }
        }

        public void ZoomOut()
        {
            if (!IsConnected || m_lUserID < 0)
            {
                return;
            }

            try
            {
                // 방법 1: 직접 제어 API 사용
                Models.CCTV.NET_DVR_PTZControl_Other(m_lUserID, channelNumber, (uint)Models.CCTV.PTZ_ZOOM_OUT, 0);
                System.Threading.Thread.Sleep(200);
                Models.CCTV.NET_DVR_PTZControl_Other(m_lUserID, channelNumber, (uint)Models.CCTV.PTZ_ZOOM_OUT, 1);
            }
            catch (Exception ex)
            {
                LogError($"ZoomOut 오류: {ex.Message}");
            }
        }
        
        // PTZ 미리 설정 위치 호출 메서드 추가
        public void GoToPreset(int presetIndex)
        {
            if (!IsConnected || m_lUserID < 0) return;
            
            try
            {
                // 미리 설정된 위치로 이동 (보통 1~255 사이의 값)
                if (presetIndex >= 1 && presetIndex <= 255)
                {
                    // 프리셋 호출 명령
                    Models.CCTV.NET_DVR_PTZPreset_Other(m_lUserID, channelNumber, 39, (uint)presetIndex);
                }
            }
            catch (Exception ex)
            {
                LogError($"GoToPreset {presetIndex} 오류: {ex.Message}");
            }
        }

        // 모든 스트림을 완전히 중지하는 새 메서드
        private void StopAllStreams()
        {
            try
            {
                // 현재 활성 스트림만 중지 (빠른 채널 전환을 위해)
                var handles = m_lRealHandles.ToList();
                foreach (var handle in handles)
                {
                    if (handle.Value >= 0)
                    {
                        bool result = Models.CCTV.NET_DVR_StopRealPlay(handle.Value);
                        LogMessage($"스트림 중지: 핸들 {handle.Value}, 결과: {result}");
                    }
                }
                m_lRealHandles.Clear();
                
                LogMessage("활성 스트림 중지 완료");
            }
            catch (Exception ex)
            {
                LogError($"StopAllStreams 오류: {ex.Message}");
            }
        }

        // 스트림 일시정지 메서드 (화면 전환 최적화용)
        public void PauseStream()
        {
            try
            {
                if (m_lUserID >= 0 && m_lRealHandles.Count > 0)
                {
                    LogMessage("CCTV 스트림 일시정지 시작");
                    
                    // 모든 활성 스트림을 일시정지하고 핸들 정리
                    var handlesToRemove = new List<int>();
                    foreach (var handle in m_lRealHandles.ToList())
                    {
                        if (handle.Value >= 0)
                        {
                            // 실시간 재생 중지
                            bool result = Models.CCTV.NET_DVR_StopRealPlay(handle.Value);
                            LogMessage($"스트림 일시정지: 채널 {handle.Key}, 핸들 {handle.Value}, 결과: {result}");
                            handlesToRemove.Add(handle.Key);
                        }
                    }
                    
                    // 중지된 핸들들을 Dictionary에서 제거
                    foreach (int channelNum in handlesToRemove)
                    {
                        m_lRealHandles.Remove(channelNum);
                    }
                    
                    StatusMessage = "CCTV 스트림 일시정지됨 (화면 전환)";
                    LogMessage("CCTV 스트림 일시정지 완료 - 핸들 정리됨");
                }
            }
            catch (Exception ex)
            {
                LogError($"CCTV 스트림 일시정지 오류: {ex.Message}");
            }
        }
        
        // 스트림 재개 메서드 (화면 전환 최적화용)
        public void ResumeStream()
        {
            try
            {
                if (m_lUserID >= 0 && IsConnected)
                {
                    LogMessage("CCTV 스트림 재개 시작");
                    
                    // 현재 채널의 스트림이 없으면 재시작
                    if (!m_lRealHandles.ContainsKey(channelNumber) || m_lRealHandles[channelNumber] < 0)
                    {
                        LogMessage($"채널 {channelNumber} 스트림 재시작 실행");
                        
                        // 약간의 지연 후 스트림 재시작 (SDK 안정화)
                        Thread.Sleep(200);
                        
                        // 기존 방식으로 스트림 재시작
                        StartRealPlay();
                        
                        StatusMessage = $"CCTV 스트림 재개됨 - 채널 {channelNumber}";
                        LogMessage("CCTV 스트림 재개 완료");
                    }
                    else
                    {
                        LogMessage($"채널 {channelNumber} 스트림이 이미 활성 상태");
                        StatusMessage = $"CCTV 스트림 활성 - 채널 {channelNumber}";
                    }
                }
                else
                {
                    LogMessage("CCTV 스트림 재개 실패: 연결되지 않음");
                    StatusMessage = "CCTV 재개 실패: 연결 끊김";
                }
            }
            catch (Exception ex)
            {
                LogError($"CCTV 스트림 재개 오류: {ex.Message}");
                StatusMessage = $"CCTV 재개 오류: {ex.Message}";
            }
        }
    }
}
