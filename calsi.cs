using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using YamlDotNet.Serialization;
using static CALSI.modules.progressUpdater;
using static CALSI.modules.dataStructures;
using static CALSI.modules.customFunctions;

namespace CALSI
{
    static class calsi
    {

        public static async void runCALSI(project prjObj)
        {
            MainWindow main = Application.Current.Dispatcher.Invoke(() => (MainWindow)Application.Current.MainWindow);

            // setting the scene
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // cancellation token activated in case you need to stop the process
                main.cancelAutomaticCalibrationToken = new CancellationTokenSource();
            });


            for (int indx = 0; indx < main.current_parameters.Count; indx++)
            {
                main.current_parameters[indx].adj_minimum = main.current_parameters[indx].minimum;
                main.current_parameters[indx].adj_maximum = main.current_parameters[indx].maximum;
                main.current_parameters[indx].best_cal_parameter = double.NaN;
            }

            // set up dotty plot data to track calibration
            if (main.auto_cal_config.autoDottyPlots == null)
            {
                main.auto_cal_config.autoDottyPlots = new();
            }
            if (!main.auto_cal_config.autoDottyPlots.ContainsKey(main.auto_cal_config.algorithm))
            {
                main.auto_cal_config.autoDottyPlots[main.auto_cal_config.algorithm] = new();
            }
            main.auto_cal_config.autoDottyPlots[main.auto_cal_config.algorithm].Clear();


            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                main.uiAutoStatuses.uiAutoCalStepProgresCalsi.SelectedItemStatus = Syncfusion.UI.Xaml.ProgressBar.StepStatus.Indeterminate;

                main.uiIterationsProgress.Progress = 0;
                main.uiIterationsProgress.SecondaryProgress = 0;

                main.uiAutoStatuses.uiAutoCalStepProgresCalsi.SelectedIndex = 1;
                main.uiAutoStatuses.uiSearchingSolutionsBusyCalsi.IsBusy = false;
            });

            // Define a progress reporting handler for each process
            Progress<int>[] progressReporters = new Progress<int>[numberOfProcesses];

            for (int i = 0; i < numberOfProcesses; i++)
            {
                int processNumber = i; // Capture the loop variable
                progressReporters[i] = new Progress<int>(percent =>
                {
                    updateProcessProgressInfo(processNumber, percent);
                });
            }

            parameterSamplesBank = null;

            for (int iterNumber = 0; iterNumber < main.auto_cal_config.calsi_options.iterations; iterNumber++)
            {
                if (main.cancelAutomaticCalibrationToken.Token.IsCancellationRequested)
                {
                    return;
                }


                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    main.uiIterationsProgress.Progress = iterNumber;
                    main.uiIterationsProgress.SecondaryProgress = ((iterNumber + 1) / main.auto_cal_config.calsi_options.iterations) * 100;
                });


                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    main.calibration_master_progress = new ObservableCollection<int>(new int[numberOfProcesses]);
                });

                main.calsiSettings.uiReportingSectionText.Text = $@"Iteration {iterNumber + 1}/{main.auto_cal_config.calsi_options.iterations}";

                if (parameterSamplesBank != null)
                {
                    var rowsList = new List<double?[]>();
                    int rowCount = parameterSamplesBank.GetLength(0); // Number of rows
                    int colCount = parameterSamplesBank.GetLength(1); // Number of columns

                    for (int i = 0; i < rowCount; i++)
                    {
                        var row = new double?[colCount];
                        for (int j = 0; j < colCount; j++)
                        {
                            row[j] = parameterSamplesBank[i, j];
                        }
                        rowsList.Add(row);
                    }

                    // sort by last row
                    bool ascending = true;

                    switch (main.auto_cal_config.objective_function.ToLower())
                    {
                        case "nse":
                            ascending = false; break;

                        case "kge":
                            ascending = false; break;

                        default: break;
                    }

                    if (main.cancelAutomaticCalibrationToken.IsCancellationRequested) { return; }

                    rowsList.Sort((row1, row2) =>
                    {
                        var lastColIndex = colCount - 1;
                        double? value1 = row1[lastColIndex];
                        double? value2 = row2[lastColIndex];

                        // Handling nulls
                        if (!value1.HasValue) return ascending ? -1 : 1; // nulls first if ascending, last if descending
                        if (!value2.HasValue) return ascending ? 1 : -1; // non-nulls first if ascending, last if descending

                        // Normal comparison
                        int comparisonResult = value1.Value.CompareTo(value2.Value);

                        // Invert comparison result for descending order
                        return ascending ? comparisonResult : -comparisonResult;
                    });

                    if (main.cancelAutomaticCalibrationToken.IsCancellationRequested) { return; }

                    lock (collectionLock)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (main.cancelAutomaticCalibrationToken.IsCancellationRequested) { return; }

                            for (int kumbu = 0; kumbu < main.current_parameters.Count; kumbu++)
                            {
                                main.current_parameters[kumbu].adj_minimum = 99999;
                                main.current_parameters[kumbu].adj_maximum = -99999;
                            }

                            if (main.cancelAutomaticCalibrationToken.IsCancellationRequested) { return; }


                            for (int indexRevisor_i = 0; indexRevisor_i < rowsList.Count && indexRevisor_i < main.auto_cal_config.calsi_options.rangeRefineThreshold; indexRevisor_i++)
                            {
                                for (int indexRevisor_j = 0; indexRevisor_j < rowsList[indexRevisor_i].Length - 1; indexRevisor_j++)
                                {

                                    // update min_adjusted
                                    if (rowsList[indexRevisor_i][indexRevisor_j] < main.current_parameters[indexRevisor_j].adj_minimum)
                                    {
                                        main.current_parameters[indexRevisor_j].adj_minimum = rowsList[indexRevisor_i][indexRevisor_j] ?? double.NaN;
                                    }
                                    if (main.current_parameters[indexRevisor_j].best_cal_parameter < main.current_parameters[indexRevisor_j].adj_minimum)
                                    {
                                        main.current_parameters[indexRevisor_j].adj_minimum = main.current_parameters[indexRevisor_j].best_cal_parameter;
                                    }

                                    // update max adjusted
                                    if (rowsList[indexRevisor_i][indexRevisor_j] > main.current_parameters[indexRevisor_j].adj_maximum)
                                    {
                                        main.current_parameters[indexRevisor_j].adj_maximum = rowsList[indexRevisor_i][indexRevisor_j] ?? double.NaN;
                                    }

                                    if (main.current_parameters[indexRevisor_j].best_cal_parameter > main.current_parameters[indexRevisor_j].adj_maximum)
                                    {
                                        main.current_parameters[indexRevisor_j].adj_maximum = main.current_parameters[indexRevisor_j].best_cal_parameter;
                                    }
                                }
                            }

                            for (int kumbu = 0; kumbu < main.current_parameters.Count; kumbu++)
                            {
                                double differenceValue = main.current_parameters[kumbu].adj_maximum -
                                main.current_parameters[kumbu].adj_minimum;

                                differenceValue = Math.Abs(differenceValue) * (main.auto_cal_config.calsi_options.rangeExpansionFactor / 100);
                                differenceValue = differenceValue / 2;

                                main.current_parameters[kumbu].adj_maximum += differenceValue;
                                main.current_parameters[kumbu].adj_minimum -= differenceValue;

                                if (main.current_parameters[kumbu].adj_maximum > main.current_parameters[kumbu].maximum)
                                {
                                    main.current_parameters[kumbu].adj_maximum = main.current_parameters[kumbu].maximum;
                                }

                                if (main.current_parameters[kumbu].adj_minimum < main.current_parameters[kumbu].minimum)
                                {
                                    main.current_parameters[kumbu].adj_minimum = main.current_parameters[kumbu].minimum;
                                }
                            }

                            main.ui_calibration_auto_parameters.ItemsSource = null;
                            main.ui_calibration_auto_parameters.ItemsSource = main.current_parameters;
                            main.ui_calibration_auto_parameters.DataContext = main.current_parameters;

                            // update the project variable
                            prjObj = DeepClone(main.current_project);

                        });
                    }
                }

                if (main.cancelAutomaticCalibrationToken.IsCancellationRequested) { return; }
                double[,] parameterSamples = generateLHSamples(prjObj.current_parameters, prjObj.auto_cal_config.calsi_options.batchSize);

                parameterSamplesBank = new Nullable<double>[parameterSamples.GetLength(0), prjObj.current_parameters.Count + 1]; // Using Nullable<double> or double?


                // start calibration processes to run samples
                if (main.cancelAutomaticCalibrationToken.IsCancellationRequested) { return; }
                Task mainCalibration = Task.Run(() =>
                {
                    Parallel.For(0, numberOfProcesses, (processNumber, loopState) =>
                    {
                        if (main.cancelAutomaticCalibrationToken.Token.IsCancellationRequested)
                        {
                            loopState.Stop();
                            
                            return;
                        }
                        var cloned_project = DeepClone(prjObj);


                        // Calculate the base batch size and additional lines to distribute
                        int baseBatchSize = parameterSamples.GetLength(0) / numberOfProcesses;
                        int extraLines = parameterSamples.GetLength(0) % numberOfProcesses;

                        // Distribute to processes
                        int batchSize = baseBatchSize + (processNumber < extraLines ? 1 : 0);
                        int startIndex = processNumber * baseBatchSize + Math.Min(processNumber, extraLines);

                        // Create a new 2D array for this batch
                        double?[,] batchSamples = new double?[batchSize, parameterSamples.GetLength(1)];

                        // Copy the rows from the original array to the batch array
                        for (int h = 0; h < batchSize; h++)
                        {
                            for (int j = 0; j < parameterSamples.GetLength(1); j++)
                            {
                                batchSamples[h, j] = parameterSamples[startIndex + h, j];
                            }
                        }

                        string srcDir = Path.GetFullPath(Path.Combine($@"{main.current_project.base_path}"));
                        // create_path($@"{srcDir}\..\.tmp\");
                        File.WriteAllText($@"{srcDir}\..\.tmp\p-Pars{processNumber}.txt", ShowArrayInMessageBox(batchSamples, display_: false));

                        runCalsiLoop(processNumber + 1, progressReporters[processNumber], cloned_project, main.cancelAutomaticCalibrationToken.Token, batchSamples, iterNumber);

                        if (main.cancelAutomaticCalibrationToken.Token.IsCancellationRequested)
                        {
                            return;
                        }

                    });

                }, main.cancelAutomaticCalibrationToken.Token);

                // Periodic checks within your logic, if feasible
                if (main.cancelAutomaticCalibrationToken.Token.IsCancellationRequested == null)
                {
                    break;
                }

                //wait for the calibration task to finish
                await mainCalibration;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    main.uiIterationsProgress.Progress = iterNumber + 1;
                    main.uiIterationsProgress.SecondaryProgress = ((iterNumber + 1) / main.auto_cal_config.calsi_options.iterations) * 100;
                });

                if (main.cancelAutomaticCalibrationToken.Token.IsCancellationRequested == null)
                {
                    
                    break;
                }
            }


            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                main.uiAutoStatuses.uiAutoCalStepProgresCalsi.SelectedItemStatus = Syncfusion.UI.Xaml.ProgressBar.StepStatus.Active;

                main.uiAutoStatuses.uiAutoCalStepProgresCalsi.SelectedIndex = 3;
                main.uiAutoStatuses.uiSearchingSolutionsBusyCalsi.IsBusy = false;
            });

            try
            {
                string tmpDirDeletable = Path.GetFullPath(Path.Combine($@"{main.current_project.base_path}\..\.tmp"));
                if (Directory.Exists(tmpDirDeletable))
                {
                    Directory.Delete(tmpDirDeletable, true);
                }
            }
            catch (Exception)
            {

            }

            writeParameters(main.current_project);  // define in custom functions

            main.is_model_running = true;

            try
            {
                runModel(main.current_project) // define in custom functions
            }
            catch (Exception)
            {
                main.is_model_running = false;
                // handle the exception
            }

            main.is_model_running = false;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                main.uiAutoStatuses.uiAutoCalStepProgresCalsi.SelectedIndex = 4;
                main.uiAutoStatuses.uiSearchingSolutionsBusyCalsi.IsBusy = false;
            });


            Application.Current.Dispatcher.Invoke(() =>
            {
                main.cancelAutomaticCalibrationToken.Cancel();
                main.is_calibration_auto = false;
            });
        }




        private readonly static object collectionLock = new object();
        public static Nullable<double>[,] parameterSamplesBank;


        private static void runCalsiLoop(int processNumber, IProgress<int> progress, project pj_obj, CancellationToken token, double?[,] samples, int iterationNumber)
        {
            try
            {
                // copy directories
                MainWindow main = Application.Current.Dispatcher.Invoke(() => (MainWindow)Application.Current.MainWindow);

                string srcDir = Path.GetFullPath(Path.Combine($@"{main.current_project.base_path}"));


                Application.Current.Dispatcher.Invoke(() => {
                    if (main.ui_calibration_automatic_observation_selection.SelectedIndex < 0)
                    {
                        if (main.ui_calibration_automatic_observation_selection.Items.Count > 0)
                        {
                            main.ui_calibration_automatic_observation_selection.SelectedIndex = 0;

                            main.auto_cal_config.calibrationChart = main.ui_calibration_automatic_observation_selection.SelectedValue.ToString();
                            update_project(main.current_project);   // define in custom functions 
                        }
                    }
                });

                

                copyDirectoryFiles(srcDir, $@"{srcDir}\..\.tmp\p{processNumber}", ".txt", (counter, total) =>   // define function in custom functions
                {
                    if (total != 0)
                    {
                        if (token.IsCancellationRequested)
                        {
                            
                            return; // Exit gracefully if cancellation is requested
                        }

                        double progressValue = ((double)counter / total) * 5;
                        progress.Report((int)Math.Round(progressValue));
                        Console.WriteLine($"Progress: {counter} of {total} files copied.");
                    }
                });


                pj_obj.modelDirectory = $@"{pj_obj.modelDirectory}\..\.tmp\p{processNumber}";

                if (token.IsCancellationRequested)
                {
                    
                    return; // Exit gracefully if cancellation is requested
                }

                // loop for calibration

                // run through each parameter

                int progress_counter_ = 0;
                int total_progress_goal = samples.GetLength(0);

                Dictionary<int, Dictionary<string, performanceMetrics>> performanceHistory = new Dictionary<int, Dictionary<string, performanceMetrics>>();
                Dictionary<int, Dictionary<DateTime, double>> tsHistory = new Dictionary<int, Dictionary<DateTime, double>>();

                string currModelDir = Path.GetFullPath(Path.Combine($@"{pj_obj.base_path}\[specific Sub-Directory With ModelFiles]"));

                for (int i = 0; i < samples.GetLength(0); i++)
                {

                    DateTime start_ = DateTime.Now;

                    progress_counter_++;


                    //set parameters
                    if (token.IsCancellationRequested)
                    {
                        
                        return; // Exit gracefully if cancellation is requested
                    }

                    for (int james = 0; james < samples.GetLength(1); james++)
                    {
                        pj_obj.current_parameters[james].value = samples[i, james] ?? double.NaN;
                    }

                    writeParameters(pj_obj);

                    Process process = new Process();

                    string executable_file_name = "model.exe";  // set model name here


                    process.StartInfo.FileName = $@"{Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)}\\assets\\executables\\{executable_file_name}";
                    process.StartInfo.WorkingDirectory = Path.GetFullPath($@"{pj_obj.base_path}\[specific Sub-Directory With ModelFiles]");
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.EnableRaisingEvents = true;

                    // Set output and error (asynchronous) handlers
                    process.OutputDataReceived += new DataReceivedEventHandler(calibrationErrorHandler);
                    process.ErrorDataReceived += new DataReceivedEventHandler(calibrationErrorHandler);

                    // Start process and handlers
                    process.Start();

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Register a cancellation action that kills the process if the token is cancelled
                    using (token.Register(() => process.Kill()))
                    {
                        // Wait for the process to complete or for a cancellation token to be triggered
                        process.WaitForExit();
                    }

                    // Check if the task was cancelled
                    token.ThrowIfCancellationRequested();

                    // here evaluate based on observations.
                    Dictionary<string, performanceMetrics>? performanceObj = timeseriesEvaluation.evaluatePerformance(pjObj: pj_obj);

                    if (performanceObj == null)
                    {
                        continue;
                    }

                    performanceHistory[i] = performanceObj;


                    lock (collectionLock)
                    {
                        main.auto_cal_config.autoDottyPlots[main.auto_cal_config.algorithm].Add(performanceObj);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MainWindow main = (MainWindow)Application.Current.MainWindow;

                            if (main.uiAutomaticCalibrationDetailsTabPages.SelectedIndex == 3)
                            {
                                updateAutoCalDottyPlots();
                            }
                        });
                    }

                    using (StreamWriter streamWriter = new StreamWriter(Path.GetFullPath($@"{currModelDir}/perfhistAuto.stb")))
                    {
                        Serializer serializer = new Serializer();
                        serializer.Serialize(streamWriter, performanceHistory);
                    }

                    Dictionary<DateTime, double> sim_dictionary = new Dictionary<DateTime, double>();

                    observation selectedCalibrationObservation = new observation();
                    foreach (observation obsOBJ in main.current_project.current_observations)
                    {
                        if (obsOBJ.chart_name == main.auto_cal_config.calibrationChart)
                        {
                            selectedCalibrationObservation = obsOBJ;
                        }
                    }

                    sim_dictionary = extract_time_series_and_save(
                        selectedCalibrationObservation.observed_variable,
                        selectedCalibrationObservation.object_type,
                        selectedCalibrationObservation.number.ToString(),
                        selectedCalibrationObservation.timestep,
                        pj_obj,
                        saveFile: false
                    );

                    tsHistory[i] = sim_dictionary;

                    string currentDirection = "Maximise";

                    switch (main.auto_cal_config.objective_function.ToLower())
                    {
                        case "pbias":
                            currentDirection = "AbsMinimise";
                            break;

                        case "mse":
                            currentDirection = "AbsMinimise";
                            break;

                        case "rmse":
                            currentDirection = "AbsMinimise";
                            break;

                        default:
                            break;
                    }

                    // get current best objective function
                    double objFX = double.NaN;

                    double currentNSE = double.NaN;
                    double currentKGE = double.NaN;
                    double currentMSE = double.NaN;
                    double currentRMSE = double.NaN;
                    double currentPBIAS = double.NaN;

                    if (main.auto_cal_config.multi_site == true)
                    {
                        double cummulativeSumNum = 0;
                        double cummulativeSumDen = 0;

                        bool pointlessLoop = false;

                        foreach (observation obsObj in main.current_project.current_observations)
                        {
                            if (pointlessLoop) { break; }

                            switch (main.auto_cal_config.objective_function.ToLower())
                            {
                                case "nse":
                                    if (!double.IsNaN(performanceHistory[i][obsObj.chart_name].nse))
                                    {
                                        cummulativeSumNum += (performanceHistory[i][obsObj.chart_name].nse * obsObj.weight);
                                        cummulativeSumDen += obsObj.weight;

                                    }
                                    else { pointlessLoop = true; }
                                    break;

                                case "kge":
                                    if (!double.IsNaN(performanceHistory[i][obsObj.chart_name].kge))
                                    {
                                        cummulativeSumNum += (performanceHistory[i][obsObj.chart_name].kge * obsObj.weight);
                                        cummulativeSumDen += obsObj.weight;
                                    }
                                    else { pointlessLoop = true; }
                                    break;

                                case "pbias":
                                    if (!double.IsNaN(performanceHistory[i][obsObj.chart_name].pbias))
                                    {
                                        cummulativeSumNum += (Math.Abs(performanceHistory[i][obsObj.chart_name].pbias) * obsObj.weight);
                                        cummulativeSumDen += obsObj.weight;
                                    }
                                    else { pointlessLoop = true; }
                                    break;

                                case "mse":
                                    if (!double.IsNaN(performanceHistory[i][obsObj.chart_name].mse))
                                    {
                                        cummulativeSumNum += (Math.Abs(performanceHistory[i][obsObj.chart_name].mse) * obsObj.weight);
                                        cummulativeSumDen += obsObj.weight;
                                    }
                                    else { pointlessLoop = true; }
                                    break;

                                case "rmse":
                                    if (!double.IsNaN(performanceHistory[i][obsObj.chart_name].rmse))
                                    {
                                        cummulativeSumNum += (Math.Abs(performanceHistory[i][obsObj.chart_name].rmse) * obsObj.weight);
                                        cummulativeSumDen += obsObj.weight;
                                    }
                                    else { pointlessLoop = true; }
                                    break;

                                default:
                                    break;
                            }
                        }
                        if (pointlessLoop == true) { continue; }

                        objFX = cummulativeSumNum / cummulativeSumDen;
                    }
                    else
                    {
                        currentNSE = performanceHistory[i][main.auto_cal_config.calibrationChart].nse;
                        currentKGE = performanceHistory[i][main.auto_cal_config.calibrationChart].kge;
                        currentMSE = performanceHistory[i][main.auto_cal_config.calibrationChart].mse;
                        currentRMSE = performanceHistory[i][main.auto_cal_config.calibrationChart].rmse;
                        currentPBIAS = performanceHistory[i][main.auto_cal_config.calibrationChart].pbias;


                        switch (main.auto_cal_config.objective_function.ToLower())
                        {

                            case "nse":
                                if (!double.IsNaN(performanceHistory[i][main.auto_cal_config.calibrationChart].nse))
                                {
                                    objFX = performanceHistory[i][main.auto_cal_config.calibrationChart].nse;
                                }
                                break;

                            case "kge":
                                if (!double.IsNaN(performanceHistory[i][main.auto_cal_config.calibrationChart].kge))
                                {
                                    objFX = performanceHistory[i][main.auto_cal_config.calibrationChart].kge;
                                }
                                break;

                            case "pbias":
                                if (!double.IsNaN(performanceHistory[i][main.auto_cal_config.calibrationChart].pbias))
                                {
                                    objFX = performanceHistory[i][main.auto_cal_config.calibrationChart].pbias;
                                }
                                break;

                            case "mse":
                                if (!double.IsNaN(performanceHistory[i][main.auto_cal_config.calibrationChart].mse))
                                {
                                    objFX = performanceHistory[i][main.auto_cal_config.calibrationChart].mse;
                                }
                                break;

                            case "rmse":
                                if (!double.IsNaN(performanceHistory[i][main.auto_cal_config.calibrationChart].rmse))
                                {
                                    objFX = performanceHistory[i][main.auto_cal_config.calibrationChart].rmse;
                                }
                                break;

                            default:
                                break;
                        }
                    }

                    if (token.IsCancellationRequested)
                    {
                        
                        return; // Exit gracefully if cancellation is requested
                    }

                    if (double.IsNaN(objFX))
                    {
                        continue;
                    }


                    lock (collectionLock)
                    {

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            for (int nullFinder = 0; nullFinder < parameterSamplesBank.GetLength(0); nullFinder++)
                            {
                                if (parameterSamplesBank[nullFinder, 0] == null)
                                {
                                    for (int j_transfer = 0; j_transfer < samples.GetLength(1); j_transfer++)
                                    {
                                        parameterSamplesBank[nullFinder, j_transfer] = samples[i, j_transfer];
                                    }
                                    parameterSamplesBank[nullFinder, parameterSamplesBank.GetLength(1) - 1] = objFX;

                                    break;
                                }
                            }

                            File.WriteAllText($@"{srcDir}\..\.tmp\iterPars.txt", ShowArrayInMessageBox(parameterSamplesBank, display_: false));


                            if (double.IsNaN(main.auto_cal_config.best_objective_fx_value))
                            {
                                main.auto_cal_config.best_objective_fx_value = objFX;
                                main.auto_cal_config.bestNSE = currentNSE;
                                main.auto_cal_config.bestKGE = currentKGE;
                                main.auto_cal_config.bestMSE = currentMSE;
                                main.auto_cal_config.bestRMSE = currentRMSE;
                                main.auto_cal_config.bestPBIAS = currentPBIAS;
                                for (int parIndex = 0; parIndex < main.current_parameters.Count; parIndex++)
                                {
                                    main.current_parameters[parIndex].best_cal_parameter = samples[i, parIndex] ?? double.NaN;
                                    main.current_parameters[parIndex].value = samples[i, parIndex] ?? double.NaN;
                                }
                            }

                            if (currentDirection == "Maximise")
                            {
                                if (objFX > main.auto_cal_config.best_objective_fx_value)
                                {
                                    main.auto_cal_config.bestNSE = currentNSE;
                                    main.auto_cal_config.bestKGE = currentKGE;
                                    main.auto_cal_config.bestMSE = currentMSE;
                                    main.auto_cal_config.bestRMSE = currentRMSE;
                                    main.auto_cal_config.bestPBIAS = currentPBIAS;

                                    main.auto_cal_config.best_objective_fx_value = objFX;

                                    for (int parIndex = 0; parIndex < main.current_parameters.Count; parIndex++)
                                    {
                                        main.current_parameters[parIndex].best_cal_parameter = samples[i, parIndex] ?? double.NaN;
                                        main.current_parameters[parIndex].value = samples[i, parIndex] ?? double.NaN;
                                    }

                                    // update metrics

                                    if (performanceObj != null)
                                    {
                                        foreach (observation obsObj in main.current_project.current_observations)
                                        {
                                            obsObj.nse = performanceObj[obsObj.chart_name].nse;
                                            obsObj.kge = performanceObj[obsObj.chart_name].kge;
                                            obsObj.rmse = performanceObj[obsObj.chart_name].rmse;
                                            obsObj.pbias = performanceObj[obsObj.chart_name].pbias;
                                            obsObj.mse = performanceObj[obsObj.chart_name].mse;
                                        }
                                    }
                                }
                            }
                            else if (currentDirection == "AbsMinimise")
                            {
                                if (Math.Abs(objFX) < Math.Abs(main.auto_cal_config.best_objective_fx_value))
                                {
                                    main.auto_cal_config.bestNSE = currentNSE;
                                    main.auto_cal_config.bestKGE = currentKGE;
                                    main.auto_cal_config.bestMSE = currentMSE;
                                    main.auto_cal_config.bestRMSE = currentRMSE;
                                    main.auto_cal_config.bestPBIAS = currentPBIAS;

                                    main.auto_cal_config.best_objective_fx_value = objFX;
                                    for (int parIndex = 0; parIndex < main.current_parameters.Count; parIndex++)
                                    {
                                        main.current_parameters[parIndex].best_cal_parameter = samples[i, parIndex] ?? double.NaN;
                                        main.current_parameters[parIndex].value = samples[i, parIndex] ?? double.NaN;
                                    }

                                    // update metrics

                                    if (performanceObj != null)
                                    {
                                        foreach (observation obsObj in main.current_project.current_observations)
                                        {
                                            obsObj.nse = performanceObj[obsObj.chart_name].nse;
                                            obsObj.kge = performanceObj[obsObj.chart_name].kge;
                                            obsObj.rmse = performanceObj[obsObj.chart_name].rmse;
                                            obsObj.pbias = performanceObj[obsObj.chart_name].pbias;
                                            obsObj.mse = performanceObj[obsObj.chart_name].mse;
                                        }
                                    }

                                }

                            }

                            main.ui_calibration_auto_parameters.ItemsSource = null;
                            main.ui_calibration_auto_parameters.ItemsSource = main.current_parameters;
                            main.ui_calibration_auto_parameters.DataContext = main.current_parameters;

                        });






                    }


                    if (token.IsCancellationRequested)
                    {
                        
                        return; // Exit gracefully if cancellation is requested
                    }

                    using (StreamWriter streamWriter = new StreamWriter(Path.GetFullPath($@"{currModelDir}/perfhistAuto.stb")))
                    {
                        Serializer serializer = new Serializer();
                        serializer.Serialize(streamWriter, performanceHistory);
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        progress.Report(5 + (int)Math.Round((double)(((float)progress_counter_ / (float)total_progress_goal) * 95)));
                    });

                    if (token.IsCancellationRequested)
                    {
                        
                        return; // Exit gracefully if cancellation is requested
                    }

                    int progressForIteration = (samples.GetLength(0) * iterationNumber) + (i + 1);

                    updateAutoCalProcessImprovement(processNumber, progressForIteration, objFX);

                }

                progress.Report(5 + (int)Math.Round((double)(((float)progress_counter_ / (float)total_progress_goal) * 95)));

                using (StreamWriter streamWriter = new StreamWriter(Path.GetFullPath($@"{currModelDir}/perfhistAuto.stb")))
                {
                    Serializer serializer = new Serializer();
                    serializer.Serialize(streamWriter, performanceHistory);
                }

            }

            catch (OperationCanceledException)
            {
                // clean up here




                // then
                
                return; // Exit gracefully if cancellation is requested
            }

            catch (AggregateException)
            {

            }
        }

    }
}
