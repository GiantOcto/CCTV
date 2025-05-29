using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CCTV.ViewModels;

namespace CCTV.Views
{
    /// <summary>
    /// CCTVWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class CCTVWindow : Window
    {
        private CCTVViewModel? viewModel;

        public CCTVWindow()
        {
            InitializeComponent();
            this.Loaded += CCTVWindow_Loaded;
            
            // Owner 윈도우 설정 시 위치 추적 이벤트 등록
            this.SourceInitialized += CCTVWindow_SourceInitialized;
        }

        private void CCTVWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // VideoImage 컨트롤을 사용하여 CCTVViewModel 초기화
                viewModel = new CCTVViewModel(VideoImage);
                this.DataContext = viewModel;
                
                // 윈도우가 로드되면 SDK 초기화 및 연결 시도
                viewModel.OnWindowLoaded();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"CCTV 윈도우 초기화 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CCTVWindow_Activated(object sender, EventArgs e)
        {
            // 윈도우 활성화 시 필요한 처리
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                // Owner 윈도우 이벤트 핸들러 해제
                if (this.Owner != null)
                {
                    this.Owner.LocationChanged -= Owner_LocationChanged;
                    this.Owner.SizeChanged -= Owner_SizeChanged;
                    this.Owner.StateChanged -= Owner_StateChanged;
                    System.Diagnostics.Debug.WriteLine("CCTVWindow: Owner 이벤트 핸들러 해제 완료");
                }
                
                Cleanup();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CCTV 윈도우 정리 오류: {ex.Message}");
            }
        }

        public void Cleanup()
        {
            try
            {
                viewModel?.Dispose();
                viewModel = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CCTV 리소스 정리 오류: {ex.Message}");
            }
        }

        // MainWindow에서 CCTVViewModel에 접근할 수 있도록 하는 메서드
        public CCTVViewModel? GetCCTVViewModel()
        {
            return viewModel;
        }

        // 연결 상태 확인 메서드
        public bool IsConnected()
        {
            return viewModel?.IsConnected ?? false;
        }

        // 수동 연결 메서드
        public void Connect()
        {
            try
            {
                viewModel?.ConnectCommand.Execute(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CCTV 연결 오류: {ex.Message}");
            }
        }

        // 수동 연결 해제 메서드
        public void Disconnect()
        {
            try
            {
                viewModel?.DisconnectCommand.Execute(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CCTV 연결 해제 오류: {ex.Message}");
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // CCTV 창이 닫힐 때는 숨기기만 하고 실제로는 닫지 않음
            e.Cancel = true;
            this.Hide();
        }

        private void CCTVWindow_SourceInitialized(object sender, EventArgs e)
        {
            // Owner가 설정된 후에 이벤트 핸들러 등록
            if (this.Owner != null)
            {
                this.Owner.LocationChanged += Owner_LocationChanged;
                this.Owner.SizeChanged += Owner_SizeChanged;
                this.Owner.StateChanged += Owner_StateChanged;
                System.Diagnostics.Debug.WriteLine("CCTVWindow: Owner 이벤트 핸들러 등록 완료");
            }
        }
        
        private void Owner_LocationChanged(object sender, EventArgs e)
        {
            // Owner 윈도우의 위치가 변경되면 CCTV 윈도우도 함께 이동
            if (this.Owner != null && this.IsVisible)
            {
                UpdatePositionRelativeToOwner();
                System.Diagnostics.Debug.WriteLine($"CCTVWindow: Owner 위치 변경에 따른 위치 업데이트");
            }
        }
        
        private void Owner_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Owner 윈도우의 크기가 변경되면 CCTV 윈도우 위치/크기도 조정
            if (this.Owner != null && this.IsVisible)
            {
                UpdatePositionRelativeToOwner();
                System.Diagnostics.Debug.WriteLine($"CCTVWindow: Owner 크기 변경에 따른 위치 업데이트");
            }
        }
        
        private void Owner_StateChanged(object sender, EventArgs e)
        {
            // Owner 윈도우 상태 변경에 따른 처리
            if (this.Owner != null)
            {
                if (this.Owner.WindowState == WindowState.Minimized)
                {
                    this.Hide();
                    System.Diagnostics.Debug.WriteLine("CCTVWindow: Owner 최소화로 인한 숨김");
                }
                else if (this.Owner.WindowState == WindowState.Normal || this.Owner.WindowState == WindowState.Maximized)
                {
                    if (!this.IsVisible)
                    {
                        this.Show();
                        UpdatePositionRelativeToOwner();
                        System.Diagnostics.Debug.WriteLine("CCTVWindow: Owner 복원으로 인한 표시");
                    }
                }
            }
        }
        
        private void UpdatePositionRelativeToOwner()
        {
            if (this.Owner == null) return;
            
            try
            {
                // 메인 윈도우 위치가 비정상적인 경우 처리하지 않음 (최소화된 상태 등)
                if (this.Owner.Left < -30000 || this.Owner.Top < -30000)
                {
                    System.Diagnostics.Debug.WriteLine($"Owner 윈도우 위치가 비정상적임 (Left={this.Owner.Left}, Top={this.Owner.Top}) - 위치 업데이트 건너뜀");
                    return;
                }

                // 2열 레이아웃에서 왼쪽 영상 영역 전체를 사용
                double rightPanelWidth = 350; // 우측 패널 너비
                double margin = 10; // 마진
                
                // 왼쪽 영상 영역의 위치와 크기 계산
                double windowX = this.Owner.Left + margin + 20; // 추가 왼쪽 마진
                double windowY = this.Owner.Top + 40 + 20; // 타이틀바 높이 + 상단 마진
                double windowWidth = this.Owner.Width - rightPanelWidth - (margin * 3) - 40; // 추가 마진 고려
                double windowHeight = this.Owner.Height - 40 - 30 - (margin * 2) - 40; // 타이틀바, 상태바, 추가 마진 제외
                
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
                
                // 계산된 위치가 유효한 범위인지 확인
                if (windowX < -1000 || windowY < -1000 || windowX > screenWidth + 1000 || windowY > screenHeight + 1000)
                {
                    System.Diagnostics.Debug.WriteLine($"계산된 윈도우 위치가 비정상적임 (X={windowX}, Y={windowY}) - 위치 업데이트 건너뜀");
                    return;
                }
                
                this.Left = windowX;
                this.Top = windowY;
                this.Width = windowWidth;
                this.Height = windowHeight;
                
                System.Diagnostics.Debug.WriteLine($"CCTVWindow 위치 설정: Left={windowX}, Top={windowY}, Width={windowWidth}, Height={windowHeight}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CCTVWindow 위치 업데이트 오류: {ex.Message}");
            }
        }
    }
} 