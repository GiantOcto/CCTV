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

    public MainWindow()
    {
        InitializeComponent();
        InitializeChannelSelection();
        this.Loaded += Window_Loaded;
        
        // 윈도우 활성화/비활성화 이벤트 추가
        this.Activated += MainWindow_Activated;
        this.Deactivated += MainWindow_Deactivated;
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
        if (CurrentChannelText != null)
        {
            CurrentChannelText.Text = $"현재 채널: {channelNumber}";
        }
        
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
                CurrentViewText.Text = "현재: 실시간 CCTV 화면";
                UpdateStatus("실시간 CCTV 화면으로 전환");
            }
            else
            {
                // 녹화영상 화면으로 전환
                ShowRecordingView();
                ViewToggleButton.Content = "📹 실시간 영상";
                CurrentViewText.Text = "현재: 녹화영상 재생 화면";
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
        if (_cctvWindow != null)
        {
            _cctvWindow.Show();
            PositionFullScreenWindow(_cctvWindow);
        }
        
        if (_recordingPlayerWindow != null)
        {
            System.Diagnostics.Debug.WriteLine($"ShowCCTVView: RecordingPlayerWindow 숨김 시도 - IsVisible: {_recordingPlayerWindow.IsVisible}");
            _recordingPlayerWindow.Hide();
            _recordingPlayerWindow.HideVideoWindow(); // RecordingVideoWindow도 함께 숨김
            
            // 강제로 Visibility 설정
            _recordingPlayerWindow.Visibility = Visibility.Hidden;
            System.Diagnostics.Debug.WriteLine($"ShowCCTVView: RecordingPlayerWindow 강제 숨김 완료 - IsVisible: {_recordingPlayerWindow.IsVisible}, Visibility: {_recordingPlayerWindow.Visibility}");
        }
    }
    
    // 녹화영상 화면 표시
    private void ShowRecordingView()
    {
        if (_recordingPlayerWindow != null)
        {
            System.Diagnostics.Debug.WriteLine($"ShowRecordingView: RecordingPlayerWindow 표시 시도 - IsVisible: {_recordingPlayerWindow.IsVisible}");
            
            // 강제로 Visibility 설정
            _recordingPlayerWindow.Visibility = Visibility.Visible;
            _recordingPlayerWindow.Show();
            PositionFullScreenWindow(_recordingPlayerWindow);
            _recordingPlayerWindow.ShowVideoWindow(); // RecordingVideoWindow도 함께 표시
            
            System.Diagnostics.Debug.WriteLine($"ShowRecordingView: RecordingPlayerWindow 표시 완료 - IsVisible: {_recordingPlayerWindow.IsVisible}, Visibility: {_recordingPlayerWindow.Visibility}");
        }
        
        if (_cctvWindow != null)
        {
            _cctvWindow.Hide();
        }
    }
    
    // 윈도우를 전체 영상 영역에 배치
    private void PositionFullScreenWindow(Window window)
    {
        if (window != null)
        {
            // 2열 레이아웃에서 왼쪽 영상 영역 전체를 사용
            double rightPanelWidth = 350; // 우측 패널 너비
            double margin = 10; // 마진
            
            // 왼쪽 영상 영역의 위치와 크기 계산
            double windowX = this.Left + margin + 20; // 추가 왼쪽 마진
            double windowY = this.Top + 40 + 20; // 타이틀바 높이 + 상단 마진
            double windowWidth = this.Width - rightPanelWidth - (margin * 3) - 40; // 추가 마진 고려
            double windowHeight = this.Height - 40 - 30 - (margin * 2) - 40; // 타이틀바, 상태바, 추가 마진 제외
            
            // 화면 경계 확인
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            
            if (windowX + windowWidth > screenWidth)
            {
                windowWidth = screenWidth - windowX - 10;
            }
            
            if (windowY + windowHeight > screenHeight)
            {
                windowHeight = screenHeight - windowY - 10;
            }
            
            // 최소 크기 보장
            windowWidth = Math.Max(windowWidth, 400);
            windowHeight = Math.Max(windowHeight, 300);
            
            window.Left = windowX;
            window.Top = windowY;
            window.Width = windowWidth;
            window.Height = windowHeight;
            
            System.Diagnostics.Debug.WriteLine($"전체 화면 윈도우 위치 설정: {window.GetType().Name} - Left={windowX}, Top={windowY}, Width={windowWidth}, Height={windowHeight}");
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
            _cctvViewModel?.Dispose();
            
            // CCTV 윈도우 닫기
            if (_cctvWindow != null)
            {
                _cctvWindow.Close();
            }
            
            // 녹화영상 윈도우 닫기
            if (_recordingPlayerWindow != null)
            {
                _recordingPlayerWindow.Close();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"리소스 정리 오류: {ex.Message}");
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
        // 현재 표시되고 있는 윈도우만 크기 업데이트
        if (_isShowingCCTV && _cctvWindow != null && _cctvWindow.IsVisible)
        {
            PositionFullScreenWindow(_cctvWindow);
        }
        else if (!_isShowingCCTV && _recordingPlayerWindow != null && _recordingPlayerWindow.IsVisible)
        {
            PositionFullScreenWindow(_recordingPlayerWindow);
        }
    }
    
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_cctvWindow != null && _recordingPlayerWindow != null)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                _cctvWindow.Hide();
                _recordingPlayerWindow.Hide();
            }
            else
            {
                // 현재 표시 모드에 따라 적절한 윈도우만 표시
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
            System.Diagnostics.Debug.WriteLine("MainWindow 비활성화됨");
            
            // 잠시 후에 확인하여 실제로 다른 애플리케이션으로 포커스가 이동했는지 체크
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 현재 애플리케이션의 윈도우 중 하나가 여전히 활성화되어 있는지 확인
                    bool anyAppWindowActive = false;
                    
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window.IsActive)
                        {
                            anyAppWindowActive = true;
                            System.Diagnostics.Debug.WriteLine($"애플리케이션 내 활성 윈도우 발견: {window.GetType().Name}");
                            break;
                        }
                    }
                    
                    // 애플리케이션의 어떤 윈도우도 활성화되어 있지 않으면 다른 프로그램으로 포커스 이동
                    if (!anyAppWindowActive)
                    {
                        System.Diagnostics.Debug.WriteLine("다른 프로그램으로 포커스 이동 - 자식 윈도우들 숨김");
                        
                        // 메인 윈도우가 비활성화될 때 모든 자식 윈도우 숨김
                        if (_cctvWindow != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"CCTVWindow 숨김 시도 - IsVisible: {_cctvWindow.IsVisible}");
                            _cctvWindow.Hide();
                            System.Diagnostics.Debug.WriteLine($"CCTVWindow 숨김 완료 - IsVisible: {_cctvWindow.IsVisible}");
                        }
                        
                        if (_recordingPlayerWindow != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"RecordingPlayerWindow 숨김 시도 - IsVisible: {_recordingPlayerWindow.IsVisible}");
                            _recordingPlayerWindow.Hide();
                            System.Diagnostics.Debug.WriteLine($"RecordingPlayerWindow 숨김 후 - IsVisible: {_recordingPlayerWindow.IsVisible}");
                            
                            _recordingPlayerWindow.HideVideoWindow(); // RecordingVideoWindow도 함께 숨김
                            System.Diagnostics.Debug.WriteLine("RecordingVideoWindow 숨김 완료");
                            
                            // 강제로 Visibility 설정
                            _recordingPlayerWindow.Visibility = Visibility.Hidden;
                            System.Diagnostics.Debug.WriteLine($"RecordingPlayerWindow 강제 숨김 후 - Visibility: {_recordingPlayerWindow.Visibility}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("애플리케이션 내 윈도우 간 포커스 이동 - 자식 윈도우 유지");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"지연된 비활성화 처리 오류: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow 비활성화 처리 오류: {ex.Message}");
        }
    }
}