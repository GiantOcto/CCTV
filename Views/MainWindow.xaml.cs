using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using CCTV.Views;
using CCTV.ViewModels;

namespace CCTV;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private CCTVViewModel? _cctvViewModel;
    private int _currentChannel = 33;
    private ToggleButton? _selectedChannelButton;
    
    // CCTV 윈도우 관련 필드 추가
    private CCTVWindow? _cctvWindow;
    private RecordingPlayerWindow? _recordingPlayerWindow;
    
    // 화면 전환 관련 필드 추가
    private bool _isShowingCCTV = true; // 현재 CCTV 화면을 보여주고 있는지 여부
    
    // 비활성화 감지용 타이머 추가
    private System.Windows.Threading.DispatcherTimer? _deactivationTimer;

    // 스크롤 관련 필드 추가
    private bool _isScrolling = false;
    private Point _lastPosition;
    private Point _lastMousePosition;
    private bool _isMouseDown = false;
    private System.Windows.Threading.DispatcherTimer _inertiaTimer;
    private double _scrollVelocity = 0;
    private const double DECELERATION_RATE = 0.90; // 감속률 (값이 클수록 더 오래 스크롤)
    private const double VELOCITY_THRESHOLD = 0.5; // 스크롤 중지 임계값

    public MainWindow()
    {
        InitializeComponent();
        InitializeChannelSelection();
        this.Loaded += Window_Loaded;
        
        // 윈도우 활성화/비활성화 이벤트 추가
        this.Activated += MainWindow_Activated;
        this.Deactivated += MainWindow_Deactivated;

        // 스크롤 성능 향상을 위한 설정
        RenderOptions.SetBitmapScalingMode(MainScrollViewer, BitmapScalingMode.LowQuality);
        ScrollViewer.SetIsDeferredScrollingEnabled(MainScrollViewer, true);
        
        // 관성 스크롤 타이머 초기화
        _inertiaTimer = new System.Windows.Threading.DispatcherTimer();
        _inertiaTimer.Interval = TimeSpan.FromMilliseconds(16); // 약 60fps
        _inertiaTimer.Tick += InertiaTimer_Tick;
        
        // 관성 스크롤링 활성화
        MainScrollViewer.PanningMode = PanningMode.VerticalOnly;
        MainScrollViewer.PanningDeceleration = 0.001; // 감속 값이 작을수록 더 긴 관성
        MainScrollViewer.PanningRatio = 1.0; // 스크롤링 비율
    }

    private void InitializeChannelSelection()
    {
        // 프로그램 시작 시 아무 채널도 선택하지 않음
        // 사용자가 채널 버튼을 클릭할 때만 연결됨
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            CreateCCTVWindow(); // CCTV 윈도우를 먼저 생성
            CreateRecordingPlayerWindow(); // 녹화영상 윈도우도 함께 생성
            
            // 메인 윈도우가 완전히 렌더링된 후 위치 재조정
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 초기에는 CCTV 화면만 표시
                ShowCCTVView();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
            
            InitializeCCTV(); // 그 다음 초기화 (연결은 하지 않음)
            UpdateStatus("CCTV 제어 프로그램이 시작되었습니다. 채널 버튼을 클릭하여 연결하세요.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"초기화 오류: {ex.Message}");
            MessageBox.Show($"프로그램 초기화 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InitializeCCTV()
    {
        try
        {
            // CCTV Window의 VideoImage 컨트롤을 사용
            if (_cctvWindow != null)
            {
                var videoImage = _cctvWindow.FindName("VideoImage") as Image;
                if (videoImage != null)
                {
                    _cctvViewModel = new CCTVViewModel(videoImage);
                    UpdateStatus("CCTV 시스템이 초기화되었습니다.");
                }
                else
                {
                    UpdateStatus("CCTV 윈도우의 VideoImage를 찾을 수 없습니다.");
                }
            }
            else
            {
                UpdateStatus("CCTV 윈도우가 생성되지 않았습니다.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"CCTV 초기화 오류: {ex.Message}");
        }
    }

    private void CCTVButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_cctvViewModel == null)
            {
                InitializeCCTV();
            }
            
            if (_cctvViewModel != null)
            {
                if (!_cctvViewModel.IsConnected)
                {
                    _cctvViewModel.ConnectCommand.Execute(null);
                    UpdateStatus("CCTV 연결 시도 중...");
                }
                else
                {
                    _cctvViewModel.DisconnectCommand.Execute(null);
                    UpdateStatus("CCTV 연결이 해제되었습니다.");
                }
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"CCTV 연결 오류: {ex.Message}");
        }
    }

    // 채널 선택 이벤트
    private void ChannelButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is ToggleButton button && button.Tag != null)
            {
                int selectedChannel = int.Parse(button.Tag.ToString()!);
                
                // 이전 선택된 버튼 해제
                UncheckAllChannelButtons();
                
                // 새 버튼 선택
                button.IsChecked = true;
                _selectedChannelButton = button;
                
                // CCTV 초기화 (필요한 경우)
                if (_cctvViewModel == null)
                {
                    InitializeCCTV();
                }
                
                // 연결되지 않은 상태라면 먼저 연결
                if (_cctvViewModel != null && !_cctvViewModel.IsConnected)
                {
                    UpdateStatus($"채널 {selectedChannel} 연결 중...");
                    
                    // 채널 번호 먼저 설정
                    _cctvViewModel.ChannelNumber = selectedChannel;
                    
                    // 연결 실행
                    _cctvViewModel.ConnectCommand.Execute(null);
                }
                else if (_cctvViewModel != null)
                {
                    // 이미 연결된 상태라면 채널만 변경
                    ChangeChannel(selectedChannel);
                }
                
                UpdateStatus($"채널 {selectedChannel} 버튼이 선택되었습니다.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"채널 선택 오류: {ex.Message}");
        }
    }

    private void UncheckAllChannelButtons()
    {
        // 모든 채널 버튼의 선택 상태 해제
        var channelButtons = new[] { "33", "34", "35", "36" };
        foreach (string channelStr in channelButtons)
        {
            var button = this.FindName($"Channel{channelStr}Button") as ToggleButton;
            if (button != null)
            {
                button.IsChecked = false;
            }
        }
    }

    private void ChangeChannel(int channel)
    {
        if (_cctvViewModel != null)
        {
            _cctvViewModel.ChangeChannel(channel);
            
            // 채널 표시 업데이트
            UpdateChannelDisplay(channel);
            
            UpdateStatus($"채널을 {channel}번으로 변경했습니다.");
        }
    }
    
    private void UpdateChannelDisplay(int channelNumber)
    {
        // 해당 채널 버튼을 선택 상태로 만들기
        SetChannelButtonSelected(channelNumber);
    }
    
    private void SetChannelButtonSelected(int channelNumber)
    {
        try
        {
            // 모든 버튼 해제
            UncheckAllChannelButtons();
            
            // 해당 채널 버튼 선택
            var button = this.FindName($"Channel{channelNumber}Button") as ToggleButton;
            if (button != null)
            {
                button.IsChecked = true;
                _selectedChannelButton = button;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetChannelButtonSelected 오류: {ex.Message}");
        }
    }

    // PTZ 제어 메서드들
    private void PanLeft_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _cctvViewModel?.PanLeft();
            UpdateStatus("카메라 좌측 이동");
        }
        catch (Exception ex)
        {
            UpdateStatus($"좌측 이동 오류: {ex.Message}");
        }
    }

    private void PanRight_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _cctvViewModel?.PanRight();
            UpdateStatus("카메라 우측 이동");
        }
        catch (Exception ex)
        {
            UpdateStatus($"우측 이동 오류: {ex.Message}");
        }
    }

    private void TiltUp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _cctvViewModel?.TiltUp();
            UpdateStatus("카메라 상향 이동");
        }
        catch (Exception ex)
        {
            UpdateStatus($"상향 이동 오류: {ex.Message}");
        }
    }

    private void TiltDown_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _cctvViewModel?.TiltDown();
            UpdateStatus("카메라 하향 이동");
        }
        catch (Exception ex)
        {
            UpdateStatus($"하향 이동 오류: {ex.Message}");
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _cctvViewModel?.ZoomIn();
            UpdateStatus("카메라 줌인");
        }
        catch (Exception ex)
        {
            UpdateStatus($"줌인 오류: {ex.Message}");
        }
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _cctvViewModel?.ZoomOut();
            UpdateStatus("카메라 줌아웃");
        }
        catch (Exception ex)
        {
            UpdateStatus($"줌아웃 오류: {ex.Message}");
        }
    }

    // 프리셋 제어 메서드들
    private void Preset1_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _cctvViewModel?.GoToPreset(1);
            UpdateStatus("프리셋 1 위치로 이동");
        }
        catch (Exception ex)
        {
            UpdateStatus($"프리셋 1 이동 오류: {ex.Message}");
        }
    }

    private void Preset2_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _cctvViewModel?.GoToPreset(2);
            UpdateStatus("프리셋 2 위치로 이동");
        }
        catch (Exception ex)
        {
            UpdateStatus($"프리셋 2 이동 오류: {ex.Message}");
        }
    }

    // 앱 종료 이벤트 핸들러
    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatus($"앱 종료 오류: {ex.Message}");
        }
    }

    // ESC 키로 앱 종료
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            try
            {
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                UpdateStatus($"앱 종료 오류: {ex.Message}");
            }
        }
    }

    // 창 드래그 이벤트 핸들러
    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"창 드래그 오류: {ex.Message}");
        }
    }

    // 터치 다운 이벤트 핸들러
    private void Grid_TouchDown(object sender, TouchEventArgs e)
    {
        try
        {
            this.DragMove();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"터치 드래그 오류: {ex.Message}");
        }
    }

    // 터치 조작 시작 이벤트 핸들러
    private void Grid_ManipulationStarted(object sender, ManipulationStartedEventArgs e)
    {
        try
        {
            this.DragMove();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"조작 드래그 오류: {ex.Message}");
        }
    }

    // 화면 전환 이벤트 핸들러
    private void ViewToggleButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _isShowingCCTV = !_isShowingCCTV;
            
            if (_isShowingCCTV)
            {
                // CCTV 화면으로 전환
                ShowCCTVView();
                ViewToggleButton.Content = "🎬 녹화 재생";
                UpdateStatus("실시간 CCTV 화면으로 전환");
            }
            else
            {
                // 녹화영상 화면으로 전환
                ShowRecordingView();
                ViewToggleButton.Content = "📹 실시간 영상";
                UpdateStatus("녹화영상 재생 화면으로 전환");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"화면 전환 오류: {ex.Message}");
        }
    }
    
    // CCTV 화면 표시
    private void ShowCCTVView()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("=== ShowCCTVView 시작 ===");
            
            // 1. RecordingPlayerWindow 일시정지 및 숨김
            if (_recordingPlayerWindow != null)
            {
                System.Diagnostics.Debug.WriteLine("RecordingPlayerWindow 일시정지 및 숨김 처리");
                
                // 녹화재생 일시정지 (리소스 절약)
                _recordingPlayerWindow.PausePlayback();
                
                // 화면 숨김
                _recordingPlayerWindow.Hide();
                _recordingPlayerWindow.HideVideoWindow();
                _recordingPlayerWindow.Visibility = Visibility.Hidden;
                
                System.Diagnostics.Debug.WriteLine("RecordingPlayerWindow 일시정지 및 숨김 완료");
            }
            
            // 2. CCTV 화면 표시 및 재개
            if (_cctvWindow != null)
            {
                System.Diagnostics.Debug.WriteLine("CCTV 화면 표시 및 재개 처리");
                
                _cctvWindow.Show();
                PositionFullScreenWindow(_cctvWindow);
                
                // CCTV 스트림 재개 (일시정지된 경우)
                if (_cctvViewModel != null && _cctvViewModel.IsConnected)
                {
                    _cctvViewModel.ResumeStream();
                    System.Diagnostics.Debug.WriteLine("CCTV 스트림 재개됨");
                }
            }
            
            System.Diagnostics.Debug.WriteLine("=== ShowCCTVView 완료 ===");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShowCCTVView 오류: {ex.Message}");
            UpdateStatus($"CCTV 화면 전환 오류: {ex.Message}");
        }
    }
    
    // 녹화영상 화면 표시
    private void ShowRecordingView()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("=== ShowRecordingView 시작 ===");
            
            // 1. CCTV 스트림 일시정지 및 숨김
            if (_cctvWindow != null)
            {
                System.Diagnostics.Debug.WriteLine("CCTV 스트림 일시정지 및 숨김 처리");
                
                // CCTV 스트림 일시정지 (리소스 절약)
                if (_cctvViewModel != null && _cctvViewModel.IsConnected)
                {
                    _cctvViewModel.PauseStream();
                    System.Diagnostics.Debug.WriteLine("CCTV 스트림 일시정지됨");
                }
                
                // 화면 숨김
                _cctvWindow.Hide();
            }
            
            // 2. RecordingPlayerWindow 표시 및 재개
            if (_recordingPlayerWindow != null)
            {
                System.Diagnostics.Debug.WriteLine("RecordingPlayerWindow 표시 및 재개 처리");
                
                // 화면 표시
                _recordingPlayerWindow.Visibility = Visibility.Visible;
                _recordingPlayerWindow.Show();
                PositionFullScreenWindow(_recordingPlayerWindow);
                _recordingPlayerWindow.ShowVideoWindow();
                
                // 녹화재생 재개 (일시정지된 경우)
                _recordingPlayerWindow.ResumePlayback();
                
                System.Diagnostics.Debug.WriteLine("RecordingPlayerWindow 표시 및 재개 완료");
            }
            
            System.Diagnostics.Debug.WriteLine("=== ShowRecordingView 완료 ===");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShowRecordingView 오류: {ex.Message}");
            UpdateStatus($"녹화영상 화면 전환 오류: {ex.Message}");
        }
    }
    
    // 윈도우를 전체 영상 영역에 배치
    private void PositionFullScreenWindow(Window window)
    {
        if (window != null)
        {
            System.Diagnostics.Debug.WriteLine($"=== PositionFullScreenWindow 시작 ===");
            System.Diagnostics.Debug.WriteLine($"메인 윈도우 크기: Width={this.Width}, Height={this.Height}");
            System.Diagnostics.Debug.WriteLine($"메인 윈도우 위치: Left={this.Left}, Top={this.Top}");
            System.Diagnostics.Debug.WriteLine($"WindowState: {this.WindowState}");
            
            // 2열 레이아웃에서 왼쪽 영상 영역 전체를 사용
            double rightPanelWidth = 350; // 우측 패널 너비
            double margin = 10; // 마진
            
            double windowX, windowY, windowWidth, windowHeight;
            
            if (this.WindowState == WindowState.Maximized)
            {
                // 최대화된 상태에서는 화면 작업 영역을 사용
                var workArea = SystemParameters.WorkArea;
                System.Diagnostics.Debug.WriteLine($"최대화 상태 - 작업 영역: Width={workArea.Width}, Height={workArea.Height}");
                
                windowX = workArea.Left + margin + 20;
                windowY = workArea.Top + 60; // 타이틀바와 메뉴 영역을 고려한 위치
                windowWidth = workArea.Width - rightPanelWidth - (margin * 3) - 40;
                windowHeight = workArea.Height - 60 - 30 - (margin * 2) - 40; // 상단 여백과 상태바, 마진 제외
            }
            else
            {
                // 일반 상태에서는 메인 윈도우 크기 사용
                windowX = this.Left + margin + 20;
                windowY = this.Top + 40 + 20; // 타이틀바 높이 + 상단 마진
                windowWidth = this.Width - rightPanelWidth - (margin * 3) - 40;
                windowHeight = this.Height - 40 - 30 - (margin * 2) - 40; // 타이틀바, 상태바, 추가 마진 제외
            }
            
            System.Diagnostics.Debug.WriteLine($"계산된 자식 윈도우 크기: Width={windowWidth}, Height={windowHeight}");
            System.Diagnostics.Debug.WriteLine($"계산된 자식 윈도우 위치: Left={windowX}, Top={windowY}");
            
            // 화면 경계 확인
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            
            System.Diagnostics.Debug.WriteLine($"화면 크기: Width={screenWidth}, Height={screenHeight}");
            
            if (windowX + windowWidth > screenWidth)
            {
                windowWidth = screenWidth - windowX - 10;
                System.Diagnostics.Debug.WriteLine($"화면 경계 초과로 폭 조정: Width={windowWidth}");
            }
            
            if (windowY + windowHeight > screenHeight)
            {
                windowHeight = screenHeight - windowY - 10;
                System.Diagnostics.Debug.WriteLine($"화면 경계 초과로 높이 조정: Height={windowHeight}");
            }
            
            // 최소 크기 보장
            windowWidth = Math.Max(windowWidth, 400);
            windowHeight = Math.Max(windowHeight, 300);
            
            System.Diagnostics.Debug.WriteLine($"최종 자식 윈도우 크기: Width={windowWidth}, Height={windowHeight}");
            System.Diagnostics.Debug.WriteLine($"최종 자식 윈도우 위치: Left={windowX}, Top={windowY}");
            
            window.Left = windowX;
            window.Top = windowY;
            window.Width = windowWidth;
            window.Height = windowHeight;
            
            System.Diagnostics.Debug.WriteLine($"전체 화면 윈도우 위치 설정: {window.GetType().Name} - Left={windowX}, Top={windowY}, Width={windowWidth}, Height={windowHeight}");
            System.Diagnostics.Debug.WriteLine($"=== PositionFullScreenWindow 완료 ===");
        }
    }

    // 유틸리티 메서드들
    private void UpdateStatus(string message)
    {
        try
        {
            if (StatusTextBlock != null)
            {
                StatusTextBlock.Text = $"{DateTime.Now:HH:mm:ss} - {message}";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"상태 업데이트 오류: {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("MainWindow OnClosed 시작");
            
            // 비활성화 타이머 정리
            if (_deactivationTimer != null)
            {
                _deactivationTimer.Stop();
                _deactivationTimer = null;
                System.Diagnostics.Debug.WriteLine("비활성화 타이머 정리 완료");
            }
            
            // CCTV ViewModel 정리
            if (_cctvViewModel != null)
            {
                _cctvViewModel.Dispose();
                _cctvViewModel = null;
                System.Diagnostics.Debug.WriteLine("CCTV ViewModel 정리 완료");
            }
            
            // 자식 윈도우들 정리
            if (_cctvWindow != null)
            {
                // 이벤트 핸들러 해제
                this.LocationChanged -= MainWindow_LocationChanged;
                this.SizeChanged -= MainWindow_SizeChanged;
                this.StateChanged -= MainWindow_StateChanged;
                this.Activated -= MainWindow_Activated;
                this.Deactivated -= MainWindow_Deactivated;
                
                // 윈도우 종료
                try
                {
                    _cctvWindow.Close();
                }
                catch (InvalidOperationException)
                {
                    // 이미 닫혀진 윈도우일 경우 무시
                }
                _cctvWindow = null;
                System.Diagnostics.Debug.WriteLine("CCTV 윈도우 정리 완료");
            }
            
            if (_recordingPlayerWindow != null)
            {
                // RecordingPlayerWindow의 Dispose 호출
                if (_recordingPlayerWindow is IDisposable disposableWindow)
                {
                    disposableWindow.Dispose();
                }
                
                try
                {
                    _recordingPlayerWindow.Close();
                }
                catch (InvalidOperationException)
                {
                    // 이미 닫혀진 윈도우일 경우 무시
                }
                _recordingPlayerWindow = null;
                System.Diagnostics.Debug.WriteLine("RecordingPlayer 윈도우 정리 완료");
            }
            
            // 강제 가비지 컬렉션 (개발/테스트 용도)
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            System.Diagnostics.Debug.WriteLine("MainWindow OnClosed 완료");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow 종료 중 오류: {ex.Message}");
            // 종료 중 예외는 로깅만 하고 계속 진행
        }
        finally
        {
            base.OnClosed(e);
        }
    }

    private void CreateCCTVWindow()
    {
        if (_cctvWindow == null)
        {
            _cctvWindow = new CCTVWindow();
            _cctvWindow.Owner = this;
            _cctvWindow.WindowStyle = WindowStyle.None;
            _cctvWindow.ShowInTaskbar = false;
            _cctvWindow.ShowActivated = false; // 활성화되지 않도록 설정
            _cctvWindow.Topmost = true;
            
            // 윈도우 위치 설정
            PositionCCTVWindow();
            
            // 메인 윈도우 이벤트 연결
            this.LocationChanged += MainWindow_LocationChanged;
            this.SizeChanged += MainWindow_SizeChanged;
            this.StateChanged += MainWindow_StateChanged;
            
            _cctvWindow.Show();
        }
    }

    private void CreateRecordingPlayerWindow()
    {
        if (_recordingPlayerWindow == null)
        {
            _recordingPlayerWindow = new RecordingPlayerWindow();
            _recordingPlayerWindow.Owner = this;
            _recordingPlayerWindow.WindowStyle = WindowStyle.None;
            _recordingPlayerWindow.ShowInTaskbar = false;
            _recordingPlayerWindow.ShowActivated = false; // 활성화되지 않도록 설정
            _recordingPlayerWindow.Topmost = true;
            
            // 녹화영상 윈도우 위치 설정
            PositionRecordingPlayerWindow();
            
            // 초기에는 숨김 - 화면 전환 버튼으로만 표시
            // _recordingPlayerWindow.Show(); <- 제거
        }
    }

    private void PositionRecordingPlayerWindow()
    {
        if (_recordingPlayerWindow != null)
        {
            // 2열 레이아웃에 맞춰 위치 계산
            double rightPanelWidth = 350; // 우측 패널 너비
            double margin = 10; // 마진
            
            // 왼쪽 영상 영역의 위치와 크기 계산 (CCTV 윈도우와 동일)
            double recordingX = this.Left + margin + 20; // CCTV 윈도우와 동일한 X 위치
            double recordingWidth = this.Width - rightPanelWidth - (margin * 3) - 40; // CCTV 윈도우와 동일한 너비
            double totalHeight = this.Height - 40 - 30 - (margin * 2) - 40; // 전체 사용 가능 높이
            double recordingHeight = totalHeight / 2; // 절반 높이
            
            // CCTV 윈도우의 시작 Y 위치와 높이를 기준으로 바로 아래에 배치
            double cctvStartY = this.Top + 40 + 20; // CCTV 윈도우 시작 Y 위치
            double recordingY = cctvStartY + recordingHeight + 5; // CCTV 윈도우 아래 + 5px 간격
            
            // 화면 경계 확인
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            
            if (recordingX + recordingWidth > screenWidth)
            {
                recordingWidth = screenWidth - recordingX - 10;
            }
            
            if (recordingY + recordingHeight > screenHeight)
            {
                recordingHeight = screenHeight - recordingY - 10;
            }
            
            // 최소 크기 보장
            recordingWidth = Math.Max(recordingWidth, 400);
            recordingHeight = Math.Max(recordingHeight, 150);
            
            _recordingPlayerWindow.Left = recordingX;
            _recordingPlayerWindow.Top = recordingY;
            _recordingPlayerWindow.Width = recordingWidth;
            _recordingPlayerWindow.Height = recordingHeight;
        }
    }
    
    private void PositionCCTVWindow()
    {
        if (_cctvWindow != null)
        {
            // 2열 레이아웃: 왼쪽 영상 영역(Grid.Column="0")에 CCTV 윈도우 고정
            double rightPanelWidth = 350; // 우측 패널 너비
            double margin = 10; // 마진
            
            // 왼쪽 영상 영역의 위치와 크기 계산
            double cctvX = this.Left + margin + 20; // 추가 왼쪽 마진
            double cctvY = this.Top + 40 + 20; // 타이틀바 높이 + 상단 마진
            double cctvWidth = this.Width - rightPanelWidth - (margin * 3) - 40; // 추가 마진 고려
            double totalHeight = this.Height - 40 - 30 - (margin * 2) - 40; // 타이틀바, 상태바, 추가 마진 제외
            
            // 항상 절반 높이로 설정 (녹화영상 윈도우와 공유)
            double cctvHeight = totalHeight / 2;
            
            // 화면 경계 확인
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            
            if (cctvX + cctvWidth > screenWidth)
            {
                cctvWidth = screenWidth - cctvX - 10;
            }
            
            if (cctvY + cctvHeight > screenHeight)
            {
                cctvHeight = screenHeight - cctvY - 10;
            }
            
            // 최소 크기 보장
            cctvWidth = Math.Max(cctvWidth, 400);
            cctvHeight = Math.Max(cctvHeight, 150);
            
            _cctvWindow.Left = cctvX;
            _cctvWindow.Top = cctvY;
            _cctvWindow.Width = cctvWidth;
            _cctvWindow.Height = cctvHeight;
        }
    }
    
    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"MainWindow_LocationChanged: Left={this.Left}, Top={this.Top}");
        
        // 현재 표시되고 있는 윈도우만 위치 업데이트
        if (_isShowingCCTV && _cctvWindow != null && _cctvWindow.IsVisible)
        {
            PositionFullScreenWindow(_cctvWindow);
        }
        else if (!_isShowingCCTV && _recordingPlayerWindow != null && _recordingPlayerWindow.IsVisible)
        {
            PositionFullScreenWindow(_recordingPlayerWindow);
        }
    }
    
    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"=== MainWindow_SizeChanged 호출됨 ===");
        System.Diagnostics.Debug.WriteLine($"이전 크기: Width={e.PreviousSize.Width}, Height={e.PreviousSize.Height}");
        System.Diagnostics.Debug.WriteLine($"새 크기: Width={e.NewSize.Width}, Height={e.NewSize.Height}");
        System.Diagnostics.Debug.WriteLine($"현재 WindowState: {this.WindowState}");
        System.Diagnostics.Debug.WriteLine($"_isShowingCCTV: {_isShowingCCTV}");
        
        // 현재 표시되고 있는 윈도우만 크기 업데이트
        if (_isShowingCCTV && _cctvWindow != null && _cctvWindow.IsVisible)
        {
            System.Diagnostics.Debug.WriteLine("CCTV 윈도우 크기 업데이트 시작");
            PositionFullScreenWindow(_cctvWindow);
            System.Diagnostics.Debug.WriteLine("CCTV 윈도우 크기 업데이트 완료");
        }
        else if (!_isShowingCCTV && _recordingPlayerWindow != null && _recordingPlayerWindow.IsVisible)
        {
            System.Diagnostics.Debug.WriteLine("녹화영상 윈도우 크기 업데이트 시작");
            PositionFullScreenWindow(_recordingPlayerWindow);
            System.Diagnostics.Debug.WriteLine("녹화영상 윈도우 크기 업데이트 완료");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("크기 업데이트할 자식 윈도우 없음");
            System.Diagnostics.Debug.WriteLine($"CCTV 윈도우: null={_cctvWindow == null}, IsVisible={_cctvWindow?.IsVisible}");
            System.Diagnostics.Debug.WriteLine($"녹화영상 윈도우: null={_recordingPlayerWindow == null}, IsVisible={_recordingPlayerWindow?.IsVisible}");
        }
        
        System.Diagnostics.Debug.WriteLine($"=== MainWindow_SizeChanged 완료 ===");
    }
    
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_cctvWindow != null && _recordingPlayerWindow != null)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow_StateChanged: WindowState = {this.WindowState}");
            
            if (this.WindowState == WindowState.Minimized)
            {
                System.Diagnostics.Debug.WriteLine("윈도우 최소화 - 자식 윈도우 숨김");
                _cctvWindow.Hide();
                _recordingPlayerWindow.Hide();
            }
            else if (this.WindowState == WindowState.Normal || this.WindowState == WindowState.Maximized)
            {
                System.Diagnostics.Debug.WriteLine($"윈도우 상태 변경: {this.WindowState} - 자식 윈도우 위치/크기 업데이트");
                
                // 약간의 지연을 두고 크기 업데이트 (윈도우 크기가 완전히 업데이트된 후)
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    System.Diagnostics.Debug.WriteLine($"지연된 크기 업데이트 시작 - WindowState: {this.WindowState}");
                    System.Diagnostics.Debug.WriteLine($"지연된 업데이트 시 메인 윈도우 크기: Width={this.Width}, Height={this.Height}");
                    
                    // 현재 표시 모드에 따라 적절한 윈도우만 표시하고 크기 조정
                    if (_isShowingCCTV)
                    {
                        ShowCCTVView();
                        System.Diagnostics.Debug.WriteLine("CCTV 뷰 크기 업데이트 완료");
                    }
                    else
                    {
                        ShowRecordingView();
                        System.Diagnostics.Debug.WriteLine("녹화영상 뷰 크기 업데이트 완료");
                    }
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
        }
    }

    // 윈도우 활성화/비활성화 이벤트 핸들러
    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("MainWindow 활성화됨");
            
            // 메인 윈도우가 활성화될 때 현재 표시 모드에 따라 자식 윈도우 표시
            if (_cctvWindow != null && _recordingPlayerWindow != null)
            {
                // 최소화 상태가 아닌 경우에만 자식 윈도우 표시
                if (this.WindowState != WindowState.Minimized)
                {
                    if (_isShowingCCTV)
                    {
                        ShowCCTVView();
                    }
                    else
                    {
                        ShowRecordingView();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow 활성화 처리 오류: {ex.Message}");
        }
    }
    
    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        try
        {
            // 이미 타이머가 실행 중이면 중복 실행 방지
            if (_deactivationTimer != null && _deactivationTimer.IsEnabled)
            {
                _deactivationTimer.Stop();
            }
            
            // 새 타이머 생성 (기존 타이머 재사용)
            if (_deactivationTimer == null)
            {
                _deactivationTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100) // 100ms로 단축하여 응답성 향상
                };
                _deactivationTimer.Tick += (timerSender, timerE) =>
                {
                    try
                    {
                        _deactivationTimer.Stop();
                        
                        // 효율적인 윈도우 검사 - 캐시된 결과 사용
                        bool isAppWindowActive = false;
                        
                        // 메인 윈도우 활성화 상태 체크
                        if (this.IsActive)
                        {
                            isAppWindowActive = true;
                        }
                        else
                        {
                            // 자식 윈도우들만 체크 (전체 윈도우 순회 대신)
                            if (_cctvWindow?.IsActive == true || _recordingPlayerWindow?.IsActive == true)
                            {
                                isAppWindowActive = true;
                            }
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"비활성화 체크: isAppWindowActive={isAppWindowActive}");
                        
                        if (!isAppWindowActive)
                        {
                            // 애플리케이션이 완전히 비활성화됨 - 자식 윈도우들 숨김
                            System.Diagnostics.Debug.WriteLine("애플리케이션 완전 비활성화 - 자식 윈도우 숨김");
                            
                            // CCTV 윈도우 숨김
                            if (_cctvWindow != null && _cctvWindow.IsVisible)
                            {
                                _cctvWindow.Hide();
                                System.Diagnostics.Debug.WriteLine("CCTV 윈도우 숨김");
                            }
                            
                            // RecordingPlayer 윈도우 숨김
                            if (_recordingPlayerWindow != null && _recordingPlayerWindow.IsVisible)
                            {
                                _recordingPlayerWindow.Hide();
                                System.Diagnostics.Debug.WriteLine("RecordingPlayer 윈도우 숨김");
                                
                                // RecordingVideoWindow도 강제 숨김
                                _recordingPlayerWindow.HideVideoWindow();
                            }
                        }
                    }
                    catch (Exception timerEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"비활성화 타이머 오류: {timerEx.Message}");
                    }
                };
            }
            
            // 타이머 시작
            _deactivationTimer.Start();
            
            System.Diagnostics.Debug.WriteLine("MainWindow_Deactivated: 지연된 체크 타이머 시작됨 (100ms)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow_Deactivated 오류: {ex.Message}");
        }
    }

    // 관성 스크롤 타이머 이벤트
    private void InertiaTimer_Tick(object? sender, EventArgs e)
    {
        // 스크롤 속도가 임계값보다 작으면 타이머 중지
        if (Math.Abs(_scrollVelocity) < VELOCITY_THRESHOLD)
        {
            _inertiaTimer?.Stop();
            return;
        }
        
        // 감속 적용
        _scrollVelocity *= DECELERATION_RATE;
        
        // 값이 너무 작으면 중지
        if (Math.Abs(_scrollVelocity) < VELOCITY_THRESHOLD)
        {
            _inertiaTimer?.Stop();
            return;
        }
        
        // 부드러운 스크롤 애니메이션
        double targetOffset = MainScrollViewer.VerticalOffset + _scrollVelocity;
        AnimateScroll(targetOffset);
    }
    
    private void AnimateScroll(double targetOffset)
    {
        // 범위 내로 조정
        targetOffset = Math.Max(0, Math.Min(targetOffset, MainScrollViewer.ScrollableHeight));
        
        // 현재 오프셋에서 목표 오프셋으로 애니메이션
        DoubleAnimation animation = new DoubleAnimation(
            MainScrollViewer.VerticalOffset,
            targetOffset,
            TimeSpan.FromMilliseconds(100),
            FillBehavior.Stop)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        animation.Completed += (s, e) => 
        {
            // 애니메이션이 끝난 후 명시적으로 스크롤 위치 설정
            MainScrollViewer.ScrollToVerticalOffset(targetOffset);
        };
        
        // 애니메이션 시작
        MainScrollViewer.BeginAnimation(ScrollViewerOffsetProperty, animation);
    }

    // 새로운 스크롤 이벤트 핸들러들
    private void MainScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {
        // ScrollViewer가 로드된 후 맨 아래로 스크롤
        MainScrollViewer.ScrollToEnd();
    }
    
    private void Content_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 관성 스크롤 중지
        _inertiaTimer.Stop();
        _scrollVelocity = 0;
        
        _lastMousePosition = e.GetPosition(MainScrollViewer);
        _isMouseDown = true;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }
    
    private void Content_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isMouseDown)
        {
            Point currentPosition = e.GetPosition(MainScrollViewer);
            double deltaY = _lastMousePosition.Y - currentPosition.Y;
            
            // 스크롤 속도 계산 (현재 이동 거리 = 속도)
            _scrollVelocity = deltaY * 0.7; // 속도 계수 조정
            
            // 애니메이션 없이 즉시 스크롤
            MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset + deltaY);
            
            _lastMousePosition = currentPosition;
            e.Handled = true;
        }
    }
    
    private void Content_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isMouseDown)
        {
            _isMouseDown = false;
            ((UIElement)sender).ReleaseMouseCapture();
            
            // 관성 스크롤 시작
            if (Math.Abs(_scrollVelocity) > VELOCITY_THRESHOLD)
            {
                _inertiaTimer.Start();
            }
            
            e.Handled = true;
        }
    }
    
    private void Content_TouchDown(object sender, TouchEventArgs e)
    {
        // 관성 스크롤 중지
        _inertiaTimer.Stop();
        _scrollVelocity = 0;
        
        _lastPosition = e.GetTouchPoint(MainScrollViewer).Position;
        _isScrolling = true;
        e.Handled = true;
    }
    
    private void MainScrollViewer_TouchMove(object sender, TouchEventArgs e)
    {
        if (_isMouseDown)
        {
            Point currentPosition = e.GetTouchPoint(MainScrollViewer).Position;
            double deltaY = _lastMousePosition.Y - currentPosition.Y;
            
            // 스크롤 속도 계산
            _scrollVelocity = deltaY * 0.7;
            
            // 즉시 스크롤
            MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset + deltaY);
            
            _lastMousePosition = currentPosition;
            e.Handled = true;
        }
    }
    
    private void MainScrollViewer_TouchUp(object sender, TouchEventArgs e)
    {
        _isMouseDown = false;
        
        // 관성 스크롤 시작
        if (Math.Abs(_scrollVelocity) > VELOCITY_THRESHOLD)
        {
            _inertiaTimer.Start();
        }
        
        e.Handled = true;
    }
    
    private void MainScrollViewer_TouchLeave(object sender, TouchEventArgs e)
    {
        _isMouseDown = false;
    }
    
    private void MainScrollViewer_ManipulationBoundaryFeedback(object sender, ManipulationBoundaryFeedbackEventArgs e)
    {
        // 경계에 도달했을 때 바운스 효과 방지
        e.Handled = true;
    }

    // ScrollViewer의 VerticalOffset을 애니메이션하기 위한 첨부 속성
    public static readonly DependencyProperty ScrollViewerOffsetProperty =
        DependencyProperty.RegisterAttached(
            "ScrollViewerOffset",
            typeof(double),
            typeof(MainWindow),
            new PropertyMetadata(0.0, OnScrollViewerOffsetChanged));
    
    private static void OnScrollViewerOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
        }
    }
    
    public static void SetScrollViewerOffset(ScrollViewer element, double value)
    {
        element.SetValue(ScrollViewerOffsetProperty, value);
    }
    
    public static double GetScrollViewerOffset(ScrollViewer element)
    {
        return (double)element.GetValue(ScrollViewerOffsetProperty);
    }
}