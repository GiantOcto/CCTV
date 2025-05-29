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

    public MainWindow()
    {
        InitializeComponent();
        InitializeChannelSelection();
        this.Loaded += Window_Loaded;
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
            double cctvHeight = this.Height - 40 - 30 - (margin * 2) - 40; // 타이틀바, 상태바, 추가 마진 제외
            
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
            cctvHeight = Math.Max(cctvHeight, 300);
            
            _cctvWindow.Left = cctvX;
            _cctvWindow.Top = cctvY;
            _cctvWindow.Width = cctvWidth;
            _cctvWindow.Height = cctvHeight;
        }
    }
    
    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        PositionCCTVWindow();
    }
    
    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PositionCCTVWindow();
    }
    
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_cctvWindow != null)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                _cctvWindow.Hide();
            }
            else
            {
                _cctvWindow.Show();
                PositionCCTVWindow();
            }
        }
    }
}