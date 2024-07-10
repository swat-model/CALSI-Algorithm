using System.Collections.ObjectModel;
using System.Windows;
using static CALSI.modules.dataStructures // define your data structures here;

namespace CALSI.modules
{
    class progressUpdater
    {
        public static void updateProcessProgressInfo(int processNumber, int percent)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MainWindow main = Application.Current.Dispatcher.Invoke(() => (MainWindow)Application.Current.MainWindow);

                // Calculate the overall progress using Observable Collection in Main
                main.calibration_master_progress[processNumber] = percent;
                double overallProgress = main.calibration_master_progress.Average();
            });
        }
    }
}
