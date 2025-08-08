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
            // Owner 창에서 시작된 닫기 요청인지 확인
            if (Application.Current.MainWindow?.IsLoaded == true)
            {
                e.Cancel = true;
                // 최소화 대신 창을 그대로 유지
                // this.WindowState = WindowState.Minimized;
                System.Diagnostics.Debug.WriteLine("CCTV창 닫기 요청 무시됨 - 창을 계속 표시");
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            // CCTV 창은 항상 최상위에 표시
            this.Topmost = true;
               base.OnActivated(e);
        }

        protected override void OnDeactivated(EventArgs e)
        {
            // 포커스를 잃어도 Topmost 유지
            this.Topmost = true;
            base.OnDeactivated(e);
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            // 포커스를 잃었을 때는 아무것도 하지 않음 (창을 숨기지 않음)
            base.OnLostFocus(e);
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

                // 상하 레이아웃에서 상단 영상 영역 전체를 사용
                double controlPanelHeight = 250; // 하단 제어 패널 높이
                double statusBarHeight = 30; // 상태바 높이
                double margin = 10; // 마진
                
                // 상단 영상 영역의 위치와 크기 계산
                double windowX = this.Owner.Left + margin + 20;
                double windowY = this.Owner.Top + 40 + 20; // 타이틀바 높이 + 상단 마진
                double windowWidth = this.Owner.Width - (margin * 2) - 40;
                double windowHeight = this.Owner.Height - controlPanelHeight - statusBarHeight - 40 - (margin * 2) - 40; // 전체 상단 영역 사용
                
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
                // MaxHeight가 설정되어 있으면 그 값을 초과하지 않도록 제한
                if (this.MaxHeight > 0 && windowHeight > this.MaxHeight)
                {
                    this.Height = this.MaxHeight;
                }
                else
                {
                    this.Height = windowHeight;
                }
                
                System.Diagnostics.Debug.WriteLine($"CCTVWindow 위치 설정: Left={windowX}, Top={windowY}, Width={windowWidth}, Height={windowHeight}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CCTVWindow 위치 업데이트 오류: {ex.Message}");
            }
        }
    }
} 