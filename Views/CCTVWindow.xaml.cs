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
    }
} 