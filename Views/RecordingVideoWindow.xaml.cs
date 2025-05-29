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
        }

        // VideoImage 참조를 외부에서 접근할 수 있도록 제공
        public Image VideoControl => VideoImage;
        
        // 윈도우 닫히면 리소스 정리
        private void Window_Closed(object sender, EventArgs e)
        {
            // 이 메서드는 윈도우가 독립적으로 닫힐 때 호출됩니다
        }
    }
} 