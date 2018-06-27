﻿using AeroScenery.AFS2;
using AeroScenery.Common;
using AeroScenery.Data;
using AeroScenery.Data.Mappers;
using AeroScenery.Download;
using AeroScenery.ImageProcessing;
using AeroScenery.OrthophotoSources;
using AeroScenery.OrthoPhotoSources;
using AeroScenery.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AeroScenery
{
    public class AeroSceneryManager
    {
        public MainForm mainForm;

        private BingOrthophotoSource bingOrthophotoSource;
        private GoogleOrthophotoSource googleOrthophotoSource;
        private USGSOrthophotoSource usgsOrthophotoSource;

        private DownloadManager downloadManager;

        private GeoConvertManager geoConvertManager;

        private DownloadFailedForm downloadFailedForm;

        private TileStitcher tileStitcher;

        private static AeroSceneryManager aeroSceneryManager;

        private ImageTileService imageTileService;

        private Common.Settings settings;

        private RegistryService registryService;

        private IDataRepository dataRepository;

        private GridSquareMapper gridSquareMapper;

        private AFSFileGenerator afsFileGenerator;

        private List<ImageTile> imageTiles;

        public AeroSceneryManager()
        {
            bingOrthophotoSource = new BingOrthophotoSource();
            googleOrthophotoSource = new GoogleOrthophotoSource();
            usgsOrthophotoSource = new USGSOrthophotoSource();

            downloadManager = new DownloadManager();
            geoConvertManager = new GeoConvertManager();
            imageTileService = new ImageTileService();
            tileStitcher = new TileStitcher();
            registryService = new RegistryService();
            gridSquareMapper = new GridSquareMapper();
            afsFileGenerator = new AFSFileGenerator();
            dataRepository = new SqlLiteDataRepository();

            imageTiles = null;
        }

        public Settings Settings
        {
            get
            {
                return this.settings;
            }
        }

        public static AeroSceneryManager Instance
        {
            get
            {
                if (AeroSceneryManager.aeroSceneryManager == null)
                {
                    aeroSceneryManager = new AeroSceneryManager();
                }

                return aeroSceneryManager;
            }
        }

        public void Initialize()
        {
            // Create settings if required and read them
            if (registryService.SettingsInRegistry())
            {
                this.settings = registryService.GetSettings();
            }
            else
            {
                this.settings = registryService.CreateDefaultSettings();
            }

            this.dataRepository.Settings = settings;
            this.dataRepository.UpgradeDatabase();

            this.mainForm = new MainForm();
            this.mainForm.StartStopClicked += async (sender, eventArgs) =>
            {
                if (this.mainForm.ActionsRunning)
                {
                    await StartSceneryGenerationProcessAsync(sender, eventArgs);
                }
                else
                {
                    StopSceneryGenerationProcess(sender, eventArgs);
                }
            };

            this.mainForm.ResetGridSquare += (sender, name) =>
            {
                this.ResetGridSquare(name);
            };
            
            this.mainForm.Initialize();
            Application.Run(this.mainForm);
        }

        private string GetTileDownloadDirectory(string afsGridSquareDirectory)
        {
            var tileDownloadDirectory = afsGridSquareDirectory;

            switch (this.settings.OrthophotoSource)
            {
                case OrthophotoSource.Bing:
                    tileDownloadDirectory += @"\b\";
                    break;
                case OrthophotoSource.Google:
                    tileDownloadDirectory += @"\g\";
                    break;
            }

            return tileDownloadDirectory;
        }

        private string GetOrthophotoSourcePrefix()
        {
            string prefix = "";

            switch (this.settings.OrthophotoSource)
            {
                case OrthophotoSource.Bing:
                    prefix = "b";
                    break;
                case OrthophotoSource.Google:
                    prefix = "g";
                    break;
            }

            return prefix;
        }

        public void StopSceneryGenerationProcess(object sender, EventArgs e)
        {
            downloadManager.StopDownloads();

            if (this.imageTiles != null)
            {
                this.imageTiles.Clear();
                this.imageTiles = null;
                System.GC.Collect();
            }

        }

        private void ActionsComplete()
        {
            this.mainForm.ActionsComplete();

            if (this.imageTiles != null)
            {
                this.imageTiles.Clear();
                this.imageTiles = null;
                System.GC.Collect();
            }

        }


        public async Task StartSceneryGenerationProcessAsync(object sender, EventArgs e)
        {
            try
            {
                int i = 0;
                foreach (AFS2GridSquare afs2GridSquare in this.mainForm.SelectedAFS2GridSquares.Values.Select(x => x.AFS2GridSquare))
                {
                    this.mainForm.UpdateParentTaskLabel(String.Format("Working on AFS Grid Square {0} of {1}", i + 1, this.mainForm.SelectedAFS2GridSquares.Count()));

                    // Do we have a directory for this afs grid square in our working directory?
                    var afsGridSquareDirectory = this.settings.WorkingDirectory + afs2GridSquare.Name;

                    if (!Directory.Exists(this.settings.WorkingDirectory + afs2GridSquare.Name))
                    {
                        Directory.CreateDirectory(this.settings.WorkingDirectory + afs2GridSquare.Name);
                    }

                    var tileDownloadDirectory = GetTileDownloadDirectory(afsGridSquareDirectory) + this.settings.ZoomLevel + @"\";
                    var stitchedTilesDirectory = GetTileDownloadDirectory(afsGridSquareDirectory) + this.settings.ZoomLevel + @"-stitched\";

                    // Do we have a directory for the afs grid square and this orthophoto source
                    if (!Directory.Exists(tileDownloadDirectory))
                    {
                        Directory.CreateDirectory(tileDownloadDirectory);
                    }

                    if (!Directory.Exists(stitchedTilesDirectory))
                    {
                        Directory.CreateDirectory(stitchedTilesDirectory);
                    }

                    // Download Imamge Tiles
                    if (this.Settings.DownloadImageTiles && this.mainForm.ActionsRunning)
                    {
                        this.mainForm.UpdateChildTaskLabel("Calculating Image Tiles To Download");


                        var imageTilesTask = Task.Run(() => {

                            // Get a list of all the image tiles we need to download
                            switch (settings.OrthophotoSource)
                            {
                                case OrthophotoSource.Bing:
                                    imageTiles = bingOrthophotoSource.ImageTilesForGridSquares(afs2GridSquare, settings.ZoomLevel);
                                    break;
                                case OrthophotoSource.Google:
                                    imageTiles = googleOrthophotoSource.ImageTilesForGridSquares(afs2GridSquare, settings.ZoomLevel);
                                    break;
                                case OrthophotoSource.USGS:
                                    imageTiles = usgsOrthophotoSource.ImageTilesForGridSquares(afs2GridSquare, settings.ZoomLevel);
                                    break;
                            }
                        });

                        await imageTilesTask;

                        this.mainForm.UpdateChildTaskLabel("Downloading Image Tiles");

                        // Capture the progress of each thread
                        var downloadThreadProgress = new Progress<DownloadThreadProgress>();
                        downloadThreadProgress.ProgressChanged += DownloadThreadProgress_ProgressChanged;

                        // Send the image tiles to the download manager
                        await downloadManager.DownloadImageTiles(imageTiles, downloadThreadProgress, tileDownloadDirectory);

                        // Only finalise if we weren't cancelled
                        if (this.mainForm.ActionsRunning)
                        {
                            var existingGridSquare = this.dataRepository.FindGridSquare(afs2GridSquare.Name);

                            if (existingGridSquare == null)
                            {
                                this.dataRepository.CreateGridSquare(this.gridSquareMapper.ToModel(afs2GridSquare));
                                this.mainForm.AddDownloadedGridSquare(afs2GridSquare);
                            }
                        }


                    }

                    // Stitch Image Tiles
                    if (this.Settings.StitchImageTiles && this.mainForm.ActionsRunning)
                    {
                        this.mainForm.UpdateChildTaskLabel("Stitching Image Tiles");

                        // Capture the progress of the tile stitcher
                        var tileStitcherProgress = new Progress<TileStitcherProgress>();
                        tileStitcherProgress.ProgressChanged += TileStitcherProgress_ProgressChanged;

                        await this.tileStitcher.StitchImageTilesAsync(tileDownloadDirectory, stitchedTilesDirectory, true, tileStitcherProgress);
                    }

                    // Generate AID and TMC Files
                    if (this.Settings.GenerateAIDAndTMCFiles && this.mainForm.ActionsRunning)
                    {
                        this.mainForm.UpdateChildTaskLabel("Generating AFS Metadata Files");

                        // Capture the progress of the tile stitcher
                        var afsFileGeneratorProgress = new Progress<AFSFileGeneratorProgress>();
                        afsFileGeneratorProgress.ProgressChanged += AFSFileGeneratorProgress_ProgressChanged;


                        // Generate AID files for the image tiles
                        await afsFileGenerator.GenerateAFSFilesAsync(afs2GridSquare, stitchedTilesDirectory, GetTileDownloadDirectory(afsGridSquareDirectory), afsFileGeneratorProgress);
                    }

                    //// Have all image tiles been downloaded by the download manager
                    //if (AllImageTilesDownloaded(imageTiles))
                    //{
                    //    // Generate the TMC File
                    //    tmcFileGenerator.GenerateTMCFile(this.mainForm.SelectedAFS2GridSquares);

                    //    // Run Geoconvert
                    //    //geoConvertManager.RunGeoConvert();
                    //}
                    //else
                    //{
                    //    // The jpg to AID count doesn't match, something is wrong, show the dialog
                    //    downloadFailedForm = new DownloadFailedForm();
                    //    downloadFailedForm.ShowDialog();
                    //}
                    i++;

                }

                // Move on to running Geoconvert for each tile
                this.StartGeoConvertProcess();

                this.ActionsComplete();

            }
            finally
            {
                if (this.imageTiles != null)
                {
                    this.imageTiles.Clear();
                    this.imageTiles = null;
                    System.GC.Collect();
                }
            }


        }

        public void StartGeoConvertProcess()
        {
            if (String.IsNullOrEmpty(this.settings.AFS2SDKDirectory))
            {
                DialogResult result = MessageBox.Show("Please set the location of the Aerofly SDK in Settings before running Geoconvert",
                    "AeroScenery",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else
            {
                int i = 0;
                // Run the Geoconvert process for each selected grid square
                foreach (AFS2GridSquare afs2GridSquare in this.mainForm.SelectedAFS2GridSquares.Values.Select(x => x.AFS2GridSquare))
                {
                    this.mainForm.UpdateParentTaskLabel(String.Format("Working on AFS Grid Square {0} of {1}", i + 1, this.mainForm.SelectedAFS2GridSquares.Count()));

                    // Do we have a directory for this afs grid square in our working directory?
                    var afsGridSquareDirectory = this.settings.WorkingDirectory + afs2GridSquare.Name;

                    if (Directory.Exists(afsGridSquareDirectory))
                    {
                        var stitchedTilesDirectory = GetTileDownloadDirectory(afsGridSquareDirectory) + this.settings.ZoomLevel + @"-stitched\";
                        var tmcFilename = String.Format("{0}{1}_{2}_stitch.tmc", stitchedTilesDirectory, this.GetOrthophotoSourcePrefix(), settings.ZoomLevel);

                        if (Directory.Exists(stitchedTilesDirectory))
                        {
                            // Run GeoConvert
                            if (this.Settings.RunGeoConvert)
                            {
                                this.mainForm.UpdateChildTaskLabel("Running GeoConvert");

                                string geoconvertPath = String.Format("{0}\\aerofly_fs_2_geoconvert", settings.AFS2SDKDirectory);
                                string geoconvertFilename = String.Format("{0}\\aerofly_fs_2_geoconvert.exe", geoconvertPath);

                                var proc = new Process
                                {
                                    StartInfo = new ProcessStartInfo
                                    {
                                        FileName = geoconvertFilename,
                                        Arguments = tmcFilename,
                                        UseShellExecute = true,
                                        RedirectStandardOutput = false,
                                        CreateNoWindow = false,
                                        WorkingDirectory = geoconvertPath
                                    }
                                };

                                proc.Start();

                            }

                            // Delete Stitched Immage Tiles
                            //if (this.Settings.DeleteStitchedImageTiles)
                            //{
                            //    this.mainForm.UpdateChildTaskLabel("Deleting Stitched Image Tiles");

                            //    // If we haven't just downloaded image tiles we need to load aero files to get image tile objects
                            //    if (imageTiles == null)
                            //    {
                            //        imageTiles = await this.imageTileService.LoadImageTilesAsync(tileDownloadDirectory);
                            //    }

                            //}

                            // Install Scenery
                            //if (this.Settings.InstallScenery)
                            //{
                            //    this.mainForm.UpdateChildTaskLabel("Prompting To Install Scenery");

                            //}

                        }
                        else
                        {
                            // Tile download directory does not exist
                        }
                    }
                    else
                    {
                        // Working directory does not exist
                    }

                    i++;
                }
            }



        }

        private void ResetGridSquare(string gridSquareName)
        {
            var existingGridSquare = this.dataRepository.FindGridSquare(gridSquareName);

            if (existingGridSquare != null)
            {
                this.dataRepository.DeleteGridSquare(gridSquareName);
            }
        }


        private void DownloadThreadProgress_ProgressChanged(object sender, DownloadThreadProgress progress)
        {
            if (this.mainForm.ActionsRunning)
            {
                var progressControl = this.mainForm.GetDownloadThreadProgressControl(progress.DownloadThreadNumber);
                var percentageProgress = (int)Math.Floor(((double)progress.FilesDownloaded / (double)progress.TotalFiles) * 100);

                progressControl.SetProgressPercentage(percentageProgress);

                progressControl.SetImageTileCount(progress.FilesDownloaded, progress.TotalFiles);

                var downloadActionProgressPercentage = this.mainForm.CurrentActionProgressPercentage;

                if (percentageProgress > downloadActionProgressPercentage)
                {
                    this.mainForm.CurrentActionProgressPercentage = percentageProgress;
                }
            }

        }


        private void TileStitcherProgress_ProgressChanged(object sender, TileStitcherProgress progress)
        {
            if (this.mainForm.ActionsRunning)
            {

                var currentStitchedImagePercentage = ((double)progress.CurrentStitchedImage / (double)progress.TotalStitchedImages);
                var nextStitchedImagePercentage = ((double)(progress.CurrentStitchedImage + 1) / (double)progress.TotalStitchedImages);

                var tilesPercentage = ((double)(progress.CurrentTilesRenderedForCurrentStitchedImage) / (double)progress.TotalImageTilesForCurrentStitchedImage);

                var percentageIncreaseBetweenThisStitchedImageAndNext = nextStitchedImagePercentage - currentStitchedImagePercentage;

                var finalPercentageDbl = (currentStitchedImagePercentage + (percentageIncreaseBetweenThisStitchedImageAndNext * tilesPercentage)) * 100;
                //Debug.WriteLine(finalPercentageDbl);

                var finalPercentage = (int)Math.Floor(finalPercentageDbl);

                if (finalPercentage > 100)
                {
                    finalPercentage = 100;
                }

                this.mainForm.CurrentActionProgressPercentage = finalPercentage;

            }

        }

        private void AFSFileGeneratorProgress_ProgressChanged(object sender, AFSFileGeneratorProgress progress)
        {
            if (this.mainForm.ActionsRunning)
            {
                var precentDone = ((double)progress.FilesCreated / (double)progress.TotalFiles) * 100;

                this.mainForm.CurrentActionProgressPercentage = (int)precentDone;

            }
        }

        public bool AllImageTilesDownloaded(List<ImageTile> imageTiles)
        {
            return true;
        }

        public void SaveSettings()
        {
            this.registryService.SaveSettings(this.settings);
        }

    }
}
