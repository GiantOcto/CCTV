using System;
using System.Windows;
using System.Windows.Controls;

namespace CCTV.Views
{
    public partial class RecordingVideoWindow : Window
    {
        public RecordingVideoWindow()
        {
            InitializeComponent();
            
            // Owner 변경 시 이벤트 핸들러 연결
            this.SourceInitialized += RecordingVideoWindow_SourceInitialized;
        }

        private void RecordingVideoWindow_SourceInitialized(object sender, EventArgs e)
        {
            // Owner의 이벤트 핸들러 연결
            if (this.Owner != null)
            {
                this.Owner.LocationChanged += Owner_LocationChanged;
                this.Owner.SizeChanged += Owner_SizeChanged;
                this.Owner.StateChanged += Owner_StateChanged;
            }
        }

        private void Owner_LocationChanged(object sender, EventArgs e)
        {
            // Owner 윈도우가 움직이면 자동으로 따라감
            // UpdatePosition() 메서드가 있다면 호출, 없다면 기본 처리
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                // Owner가 RecordingPlayerWindow인 경우 
                if (this.Owner is RecordingPlayerWindow playerWindow)
                {
                    // RecordingPlayerWindow의 UpdateVideoWindowPosition 메서드를 통해 위치 업데이트
                    // 직접 접근 가능한 경우만 처리
                }
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        private void Owner_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Owner 크기 변경 시 처리
            if (this.Owner != null && this.IsVisible)
            {
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 필요한 경우 크기나 위치 조정 로직 추가
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
        }

        private void Owner_StateChanged(object sender, EventArgs e)
        {
            // Owner 상태 변경 시 따라서 변경
            if (this.Owner != null)
            {
                if (this.Owner.WindowState == WindowState.Minimized)
                {
                    this.Hide();
                }
                else if (this.Owner.WindowState == WindowState.Normal || 
                         this.Owner.WindowState == WindowState.Maximized)
                {
                    if (!this.IsVisible)
                    {
                        this.Show();
                    }
                }
            }
        }

        // VideoImage 참조를 외부에서 접근할 수 있도록 제공
        public Image VideoControl => VideoImage;
        
        // 윈도우 닫히면 리소스 정리
        private void Window_Closed(object sender, EventArgs e)
        {
            // 이벤트 핸들러 정리
            if (this.Owner != null)
            {
                this.Owner.LocationChanged -= Owner_LocationChanged;
                this.Owner.SizeChanged -= Owner_SizeChanged;
                this.Owner.StateChanged -= Owner_StateChanged;
            }
        }
    }
} 