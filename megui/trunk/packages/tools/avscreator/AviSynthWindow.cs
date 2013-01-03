// ****************************************************************************
// 
// Copyright (C) 2005-2013 Doom9 & al
// 
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
// 
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

using MeGUI.core.util;

namespace MeGUI
{

    /// <summary>
	/// Summary description for AviSynthWindow.
	/// </summary>
	public partial class AviSynthWindow : Form
	{
		#region variable declaration
        private string originalScript;
        private bool originalInlineAvs;
        private bool isPreviewMode = false;
        private bool eventsOn = true;
		private VideoPlayer player;
        private IMediaFile file;
        private IVideoReader reader;
		private StringBuilder script;
		public event OpenScriptCallback OpenScript;
        private Dar? suggestedDar;
        private MainForm mainForm;
        private PossibleSources sourceType;
        private SourceDetector detector;
        private string indexFile;
        private int scriptRefresh = 1; // >= 1 enabled; < 1 disabled
        private bool bAllowUpsizing;
		#endregion

		#region construction/deconstruction
        public AviSynthWindow(MainForm mainForm)
        {
            scriptRefresh--;
            eventsOn = false;
            this.mainForm = mainForm;
			InitializeComponent();

            this.controlsToDisable = new List<Control>();
            this.controlsToDisable.Add(reopenOriginal);
            this.controlsToDisable.Add(filtersGroupbox);
            this.controlsToDisable.Add(deinterlacingGroupBox);
            this.controlsToDisable.Add(mpegOptGroupBox);
            this.controlsToDisable.Add(aviOptGroupBox);
            this.controlsToDisable.Add(resNCropGroupbox);
            this.controlsToDisable.Add(previewAvsButton);
            this.controlsToDisable.Add(saveButton);
            this.controlsToDisable.Add(arChooser);
            this.controlsToDisable.Add(inputDARLabel);
            this.controlsToDisable.Add(signalAR);
            this.controlsToDisable.Add(avisynthScript);
            this.controlsToDisable.Add(openDLLButton);
            this.controlsToDisable.Add(dgOptions);
            enableControls(false);
            script = new StringBuilder();

            this.resizeFilterType.Items.Clear();
            this.resizeFilterType.DataSource = ScriptServer.ListOfResizeFilterType;
            this.resizeFilterType.BindingContext = new BindingContext();
            this.noiseFilterType.Items.Clear();
            this.noiseFilterType.DataSource = ScriptServer.ListOfDenoiseFilterType;
            this.noiseFilterType.BindingContext = new BindingContext();
            this.deintFieldOrder.Items.Clear();
            this.deintFieldOrder.DataSource = ScriptServer.ListOfFieldOrders;
            this.deintFieldOrder.BindingContext = new BindingContext();
            this.deintSourceType.Items.Clear();
            this.deintSourceType.DataSource = ScriptServer.ListOfSourceTypes;
            this.deintSourceType.BindingContext = new BindingContext();
            this.cbNvDeInt.Items.Clear();
            this.cbNvDeInt.DataSource = ScriptServer.ListOfNvDeIntType;
            this.cbNvDeInt.BindingContext = new BindingContext();
            
            deintFieldOrder.SelectedIndex = -1;
            deintSourceType.SelectedIndex = -1;
            cbNvDeInt.SelectedIndex = 0;
            cbCharset.SelectedIndex = 0;
            modValueBox.SelectedIndex = 0;
            bAllowUpsizing = false;

            this.originalScript = String.Empty;
            this.isPreviewMode = false;

			player = null;
			this.crop.Checked = false;
			this.cropLeft.Value = 0;
			this.cropTop.Value = 0;
			this.cropRight.Value = 0;
			this.cropBottom.Value = 0;

            deinterlaceType.DataSource = new DeinterlaceFilter[] { new DeinterlaceFilter("Do nothing (source not detected)", "#blank deinterlace line") };

            avsProfile.Manager = MainForm.Instance.Profiles;

            eventsOn = true;
            updateEverything(true, true);
		}

        void ProfileChanged(object sender, EventArgs e)
        {
            this.Settings = GetProfileSettings();
        }

		/// <summary>
		/// constructor that first initializes everything using the default constructor
		/// then opens a preview window with the video given as parameter
		/// </summary>
		/// <param name="videoInput">the DGIndex script to be loaded</param>
		public AviSynthWindow(MainForm mainForm, string videoInput) : this(mainForm)
		{
            scriptRefresh--;
            openVideoSource(videoInput, null);
            updateEverything(true, true);
		}

        public AviSynthWindow(MainForm mainForm, string videoInput, string indexFile)
            : this(mainForm)
        {
            scriptRefresh--;
            openVideoSource(videoInput, indexFile);
            updateEverything(true, true);
        }

		protected override void OnClosing(CancelEventArgs e)
		{
            if (player != null)
				player.Close();
            if (detector != null)
                detector.stop();
            detector = null;
			base.OnClosing (e);
		}
		#endregion

        #region buttons
        private void input_FileSelected(FileBar sender, FileBarEventArgs args)
        {
            scriptRefresh--;
            openVideoSource(input.Filename, null);
            updateEverything(true, true);
		}

		private void openDLLButton_Click(object sender, System.EventArgs e)
		{
            this.openFilterDialog.InitialDirectory = MainForm.Instance.Settings.AvisynthPluginsPath;
			if (this.openFilterDialog.ShowDialog() == DialogResult.OK)
			{
				dllPath.Text = openFilterDialog.FileName;
                string temp = avisynthScript.Text;
				script = new StringBuilder();
				script.Append("LoadPlugin(\"" + openFilterDialog.FileName + "\")\r\n");
				script.Append(temp);
				avisynthScript.Text = script.ToString();
			}
		}

		private void previewButton_Click(object sender, System.EventArgs e)
		{
            // If the player is null, create a new one.
            // Otherwise use the existing player to load the latest preview.
            if (player == null || player.IsDisposed) 
                player = new VideoPlayer();

			if (player.loadVideo(mainForm, avisynthScript.Text, PREVIEWTYPE.REGULAR, false, true, player.CurrentFrame, true))
			{
				player.disableIntroAndCredits();
                reader = player.Reader;
                isPreviewMode = true;
                sendCropValues();
                if (this.Visible)
                    player.Show();
                player.SetScreenSize();
                this.TopMost = player.TopMost = true;
                if (!mainForm.Settings.AlwaysOnTop)
                    this.TopMost = player.TopMost = false;
			}
		}
		
        private void saveButton_Click(object sender, System.EventArgs e)
		{
            string fileName = videoOutput.Filename;
            writeScript(fileName);
			if (onSaveLoadScript.Checked)
			{
                if(player != null)
				    player.Close();
				this.Close();
                OpenScript(fileName);
            }
		}
		#endregion

		#region script generation
		private string generateScript()
		{
			script = new StringBuilder();
            //scriptLoad = new StringBuilder(); Better to use AviSynth plugin dir and it is easier for avs templates/profiles
			
			string inputLine = "#input";
			string deinterlaceLines = "#deinterlace";
			string denoiseLines = "#denoise";
			string cropLine = "#crop";
			string resizeLine = "#resize";

            double fps = (double)fpsBox.Value;
            inputLine = ScriptServer.GetInputLine(this.input.Filename, 
                                                  this.indexFile,
                                                  deinterlace.Checked, 
                                                  sourceType, 
                                                  colourCorrect.Checked, 
                                                  mpeg2Deblocking.Checked, 
                                                  flipVertical.Checked, 
                                                  fps,
                                                  dss2.Checked);

            if (nvDeInt.Enabled)
            {
                if (nvDeInt.Checked)
                    inputLine += ScriptServer.GetNvDeInterlacerLine(nvDeInt.Checked, (NvDeinterlacerType)(cbNvDeInt.SelectedItem as EnumProxy).RealValue);
                if (nvResize.Checked)
                    inputLine += ", resize_w=" + horizontalResolution.Value.ToString() + ", resize_h=" + verticalResolution.Value.ToString();                
                inputLine += ")";
            }
            if (deinterlace.Checked && deinterlaceType.SelectedItem is DeinterlaceFilter)
                deinterlaceLines = ((DeinterlaceFilter)deinterlaceType.SelectedItem).Script;
            cropLine = ScriptServer.GetCropLine(crop.Checked, Cropping);
            
            // resize options
            int iWidth = (int)horizontalResolution.Maximum;
            int iHeight = (int)verticalResolution.Maximum;
            if (file != null)
            {
                iWidth = (int)file.VideoInfo.Width;
                iHeight = (int)file.VideoInfo.Height;
            }
            if (!nvResize.Checked)
                resizeLine = ScriptServer.GetResizeLine(resize.Checked, (int)horizontalResolution.Value, (int)verticalResolution.Value, 0, 0, (ResizeFilterType)(resizeFilterType.SelectedItem as EnumProxy).RealValue,
                                                        crop.Checked, Cropping, iWidth, iHeight);

            denoiseLines = ScriptServer.GetDenoiseLines(noiseFilter.Checked, (DenoiseFilterType)(noiseFilterType.SelectedItem as EnumProxy).RealValue);

            string newScript = ScriptServer.CreateScriptFromTemplate(GetProfileSettings().Template, inputLine, cropLine, resizeLine, denoiseLines, deinterlaceLines);

            if (this.signalAR.Checked && suggestedDar.HasValue)
                newScript = string.Format("# Set DAR in encoder to {0} : {1}. The following line is for automatic signalling\r\nglobal MeGUI_darx = {0}\r\nglobal MeGUI_dary = {1}\r\n",
                    suggestedDar.Value.X, suggestedDar.Value.Y) + newScript;

            if (this.SubtitlesPath.Text != "")
            {
                newScript += "\r\nLoadPlugin(\"" + Path.Combine(MainForm.Instance.Settings.AvisynthPluginsPath, "VSFilter.dll") + "\")";
                if (cbCharset.Enabled)
                {
                    string charset = CharsetValue();
                    newScript += "\r\nTextSub(\"" + SubtitlesPath.Text + "\"" + ", " + charset + ")\r\n";
                }
                else
                    newScript += "\r\nVobSub(\"" + SubtitlesPath.Text + "\")\r\n";
            }
            return newScript;
		}

        private AviSynthSettings GetProfileSettings()
        {
            return (AviSynthSettings)avsProfile.SelectedProfile.BaseSettings;
        }

		private void showScript(bool bForce)
		{
            if (bForce)
                scriptRefresh++;
            if (scriptRefresh < 1)
                return;

            string oldScript = avisynthScript.Text;
            avisynthScript.Text = this.generateScript();
            if (!oldScript.Equals(avisynthScript.Text))
                chAutoPreview_CheckedChanged(null, null);
		}
		#endregion

		#region helper methods
        /// <summary>
        /// Opens a video source using the correct method based on the extension of the file name
        /// </summary>
        /// <param name="videoInput"></param>
        private void openVideoSource(string videoInput, string indexFileTemp)
        {
            string ext, projectPath, fileNameNoPath;

            indexFile = indexFileTemp;
            projectPath = mainForm.Settings.DefaultOutputDir;
            if (String.IsNullOrEmpty(indexFile))
            {
                ext = Path.GetExtension(videoInput).ToLower(System.Globalization.CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(projectPath))
                    projectPath = Path.GetDirectoryName(videoInput);
                fileNameNoPath = Path.GetFileName(videoInput);
            }
            else
            {
                ext = Path.GetExtension(indexFile).ToLower(System.Globalization.CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(projectPath))
                    projectPath = Path.GetDirectoryName(indexFile);
                fileNameNoPath = Path.GetFileName(indexFile);
            }           
            videoOutput.Filename = Path.Combine(projectPath, Path.ChangeExtension(fileNameNoPath, ".avs"));

            switch (ext)
            {
                case ".avs":
                    sourceType = PossibleSources.avs;
                    videoOutput.Filename = Path.Combine(projectPath, Path.ChangeExtension(fileNameNoPath, "_new.avs")); // to avoid overwritten
                    openAVSScript(videoInput);
                    break;           
                case ".d2v":
                    sourceType = PossibleSources.d2v;
                    openVideo(videoInput);
                    break;
                case ".dga":
                    sourceType = PossibleSources.dga;                    
                    openVideo(videoInput);
                    break;
                case ".dgi":
                    sourceType = PossibleSources.dgi;
                    openVideo(videoInput); 
                    break;
                case ".ffindex":
                    sourceType = PossibleSources.ffindex;
                    if (videoInput.ToLower(System.Globalization.CultureInfo.InvariantCulture).EndsWith(".ffindex"))
                        openVideo(videoInput.Substring(0, videoInput.Length - 8));
                    else
                        openVideo(videoInput);
                    break;
                case ".vdr":
                    sourceType = PossibleSources.vdr;
                    openVDubFrameServer(videoInput);
                    break;
                default:
                    if (File.Exists(videoInput + ".ffindex"))
                    {
                        sourceType = PossibleSources.ffindex;
                        openVideo(videoInput);
                    }
                    else
                    {
                        int iResult = mainForm.DialogManager.AVSCreatorOpen(videoInput);
                        switch (iResult)
                        {
                            case 0:
                                OneClickWindow ocmt = new OneClickWindow(mainForm);
                                ocmt.setInput(videoInput);
                                ocmt.Show();
                                this.Close();
                                break;
                            case 1:
                                FileIndexerWindow fileIndexer = new FileIndexerWindow(mainForm);
                                fileIndexer.setConfig(videoInput, null, 2, true, true, true, false);
                                fileIndexer.Show();
                                this.Close();
                                break;
                            default:
                                sourceType = PossibleSources.directShow;
                                openDirectShow(videoInput);
                                break;
                        }
                    }
                    break;
            }
            setSourceInterface();
        }
		
        /// <summary>
		/// writes the AviSynth script currently shown in the GUI to the given path
		/// </summary>
		/// <param name="path">path and name of the AviSynth script to be written</param>
		private void writeScript(string path)
		{
			try
			{
				using (StreamWriter sw = new StreamWriter(path, false, Encoding.Default))
                {
				    sw.Write(avisynthScript.Text);
				    sw.Close();
                }
			}
			catch (IOException i)
			{
				MessageBox.Show("An error occurred when trying to save the AviSynth script:\r\n" + i.Message);
			}
		}
        
        /// <summary>
        /// Set the correct states of the interface elements that are only valid for certain inputs
        /// </summary>
        private void setSourceInterface()
        {
            switch (this.sourceType)
            {            
                case PossibleSources.d2v:
                case PossibleSources.dga:
                case PossibleSources.mpeg2:
                    this.mpeg2Deblocking.Enabled = true;
                    this.colourCorrect.Enabled = true;
                    this.fpsBox.Enabled = false;
                    this.flipVertical.Enabled = false;
                    this.flipVertical.Checked = false;
                    this.cbNvDeInt.Enabled = false;
                    this.nvDeInt.Enabled = false;
                    this.nvDeInt.Checked = false;
                    this.nvResize.Enabled = false;                    
                    this.nvResize.Checked = false;
                    this.dss2.Enabled = false;
                    this.tabSources.SelectedTab = tabPage1;
                    break;
                case PossibleSources.vdr:
                case PossibleSources.avs:
                    this.mpeg2Deblocking.Checked = false;
                    this.mpeg2Deblocking.Enabled = false;
                    this.colourCorrect.Enabled = false;
                    this.colourCorrect.Checked = false;
                    this.flipVertical.Enabled = false;
                    this.flipVertical.Checked = false;
                    this.dss2.Enabled = false;
                    this.fpsBox.Enabled = false;
                    this.cbNvDeInt.Enabled = false;
                    this.nvDeInt.Enabled = false;
                    this.nvDeInt.Checked = false;
                    this.nvResize.Enabled = false;
                    this.nvResize.Checked = false;
                    this.tabSources.SelectedTab = tabPage1;
                    break;
                case PossibleSources.ffindex:
                    this.mpeg2Deblocking.Checked = false;
                    this.mpeg2Deblocking.Enabled = false;
                    this.colourCorrect.Enabled = false;
                    this.colourCorrect.Checked = false;
                    this.dss2.Enabled = false;
                    this.fpsBox.Enabled = false;
                    this.flipVertical.Enabled = true;
                    this.cbNvDeInt.Enabled = false;
                    this.nvDeInt.Enabled = false;
                    this.nvDeInt.Checked = false;
                    this.nvResize.Enabled = false;
                    this.nvResize.Checked = false;
                    this.tabSources.SelectedTab = tabPage1;
                    break;
                case PossibleSources.directShow:
                    this.mpeg2Deblocking.Checked = false;
                    this.mpeg2Deblocking.Enabled = false;
                    this.colourCorrect.Enabled = false;
                    this.colourCorrect.Checked = false;
                    this.dss2.Enabled = true;
                    this.fpsBox.Enabled = true;
                    this.flipVertical.Enabled = true;
                    this.cbNvDeInt.Enabled = false;
                    this.nvDeInt.Enabled = false;
                    this.nvDeInt.Checked = false;
                    this.nvResize.Enabled = false;
                    this.nvResize.Checked = false;
                    this.tabSources.SelectedTab = tabPage2;
                    break;
                case PossibleSources.dgi:
                    this.mpeg2Deblocking.Checked = false;
                    this.mpeg2Deblocking.Enabled = false;
                    this.colourCorrect.Enabled = false;
                    this.colourCorrect.Checked = false;
                    this.flipVertical.Enabled = false;
                    this.flipVertical.Checked = false;
                    this.dss2.Enabled = false;
                    this.fpsBox.Enabled = false;
                    this.cbNvDeInt.Enabled = false;
                    this.nvDeInt.Enabled = true;
                    this.nvDeInt.Checked = false;
                    this.nvResize.Enabled = true;
                    this.nvResize.Checked = false;
                    this.cbNvDeInt.SelectedIndex = 0;
                    this.tabSources.SelectedTab = tabPage3;
                    break;
            }
        }

        /// <summary>
        /// check whether or not it's an NV file compatible (for DGxNV tools)
        /// </summary>
        private void checkNVCompatibleFile(string input)
        {
            bool flag = false;
            using (StreamReader sr = new StreamReader(input))
            {
                string line = sr.ReadLine();
                switch (this.sourceType)
                {
                    case PossibleSources.dgi:
                        if (line.Contains("DGMPGIndexFileNV")) flag = true;
                        if (line.Contains("DGAVCIndexFileNV")) flag = true;
                        if (line.Contains("DGVC1IndexFileNV")) flag = true; 
                        break; 
                }
            }
            if (!flag)
            {
                if (MessageBox.Show("You cannot use this option with the " + Path.GetFileName(input) + " file. It's not compatible...",
                    "Information", MessageBoxButtons.OK, MessageBoxIcon.Information) == DialogResult.OK)
                {
                    this.nvDeInt.Checked = false;
                    this.nvResize.Checked = false;
                }
            }
        }

        private SourceInfo DeintInfo
        {
            get
            {
                SourceInfo info = new SourceInfo();
                try { info.sourceType = (SourceType)((EnumProxy)deintSourceType.SelectedItem).Tag; }
                catch (NullReferenceException) { info.sourceType = SourceType.UNKNOWN; }
                try { info.fieldOrder = (FieldOrder)((EnumProxy)deintFieldOrder.SelectedItem).Tag; }
                catch (NullReferenceException) { info.fieldOrder = FieldOrder.UNKNOWN; }
                info.decimateM = (int)deintM.Value;
                try
                {
                    info.majorityFilm = ((UserSourceType)((EnumProxy)deintSourceType.SelectedItem).RealValue)
                   == UserSourceType.HybridFilmInterlaced;
                }
                catch (NullReferenceException) { }
                info.isAnime = deintIsAnime.Checked;
                return info;
            }
            set
            {
                if (value.sourceType == SourceType.UNKNOWN || value.sourceType == SourceType.NOT_ENOUGH_SECTIONS)
                {
                    MessageBox.Show("Source detection couldn't determine the source type!", "Source detection failed", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }
                foreach (EnumProxy o in deintSourceType.Items)
                {
                    if ((SourceType)o.Tag == value.sourceType) deintSourceType.SelectedItem = o;
                }
                foreach (EnumProxy o in deintFieldOrder.Items)
                {
                    if ((FieldOrder)o.Tag == value.fieldOrder) deintFieldOrder.SelectedItem = o;
                }
                if (value.fieldOrder == FieldOrder.UNKNOWN) deintFieldOrder.SelectedIndex = -1;
                deintM.Value = value.decimateM;
                if (value.sourceType == SourceType.HYBRID_FILM_INTERLACED)
                {
                    if (value.majorityFilm) deintSourceType.SelectedItem = ScriptServer.ListOfSourceTypes[(int)UserSourceType.HybridFilmInterlaced];
                    else deintSourceType.SelectedItem = ScriptServer.ListOfSourceTypes[(int)UserSourceType.HybridInterlacedFilm];
                }
                this.deinterlaceType.DataSource = ScriptServer.GetDeinterlacers(value);
                this.deinterlaceType.BindingContext = new BindingContext();
            }
        }
        
        /// <summary>
        /// Check whether direct show can render the avi and then open it through an avisynth script.
        /// The avs is being used in order to allow more preview flexibility later.
        /// </summary>
        /// <param name="fileName">Input video file</param>     
        private void openDirectShow(string fileName)
        {
            if (!File.Exists(fileName))
            {
                MessageBox.Show(fileName + " could not be found", "File Not Found", MessageBoxButtons.OK);
                return;
            }
            else
            {
                DirectShow ds = new DirectShow();
                if (!ds.checkRender(fileName)) // make sure graphedit can render the file
                {
                    MessageBox.Show("Unable to render the file.\r\nYou probably don't have the correct filters installed", "Direct Show Error", MessageBoxButtons.OK);
                    return;
                }

                string tempAvs;
                if (fileName.ToLower(System.Globalization.CultureInfo.InvariantCulture).EndsWith(".avi"))
                {
                    tempAvs = "AVISource(\"" + fileName + "\", audio=false)" + VideoUtil.getAssumeFPS(0, fileName);
                }
                else
                {
                    string frameRateString = null;
                    try
                    {
                        MediaInfoFile info = new MediaInfoFile(fileName);
                        if (info.VideoInfo.HasVideo && info.VideoInfo.FPS > 0)
                            frameRateString = info.VideoInfo.FPS.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch (Exception)
                    { }

                    tempAvs = string.Format(
                        "LoadPlugin(\"{0}\")\r\nDirectShowSource(\"{1}\", audio=false{2}, convertfps=true){3}{4}",
                        Path.Combine(Path.GetDirectoryName(MainForm.Instance.Settings.AviSynthPath), "directshowsource.dll"),
                        fileName,
                        frameRateString == null ? string.Empty : (", fps=" + frameRateString),
                        VideoUtil.getAssumeFPS(0, fileName),
                        this.flipVertical.Checked ? ".FlipVertical()" : string.Empty
                        );
                } 
                if (file != null)
                    file.Dispose();
                openVideo(tempAvs, fileName, true);

            }
        }
        
        /// <summary>
        /// Create a temporary avs to wrap the frameserver file then open it as for any other avs
        /// </summary>
        /// <param name="fileName">Name of the .vdr file</param>
        private void openVDubFrameServer(string fileName)
        {
            if (!File.Exists(fileName))
            {
                MessageBox.Show(fileName + " could not be found","File Not Found",MessageBoxButtons.OK);
                return;
            }
            openVideo("AviSource(\"" + fileName + ", audio=false\")\r\n", fileName, true);
        }

        /// <summary>
        /// Create a temporary avs to wrap the frameserver file then open it as for any other avs
        /// </summary>
        /// <param name="fileName">Name of the avs script</param>
        private void openAVSScript(string fileName)
        {
            if (!File.Exists(fileName))
            {
                MessageBox.Show(fileName + " could not be found", "File Not Found", MessageBoxButtons.OK);
                return;
            }
            openVideo("Import(\"" + fileName + "\")\r\n", fileName, true);
        }

        private void enableControls(bool enable)
        {
            foreach (Control ctrl in this.controlsToDisable)
                ctrl.Enabled = enable;

            if (deintSourceType.SelectedIndex < 1)
            {
                deinterlace.Enabled = false;
                deinterlace.Checked = false;
            }
            else
                deinterlace.Enabled = true;
        }

        private void openVideo(string videoInput)
        {
            if (String.IsNullOrEmpty(indexFile))
                openVideo(videoInput, videoInput, false);
            else
                openVideo(videoInput + "|" + indexFile, videoInput, false);
        }

        private bool showOriginal()
        {
            int iCurrentFrame = -1;
            if (player == null || player.IsDisposed)
                player = new VideoPlayer();
            else
                iCurrentFrame = player.CurrentFrame;
            this.isPreviewMode = false;
            if (player.loadVideo(mainForm, originalScript, PREVIEWTYPE.REGULAR, false, originalInlineAvs, iCurrentFrame, true))
            {
                reader = player.Reader;
                sendCropValues();
                if (this.Visible)
                    player.Show();
                player.SetScreenSize();
                this.TopMost = player.TopMost = true;
                if (!mainForm.Settings.AlwaysOnTop)
                    this.TopMost = player.TopMost = false;
                return true;
            }
            else
            {
                player.Close();
                player = null;
                return false;
            }
        }

		/// <summary>
		/// opens a given script
		/// </summary>
		/// <param name="videoInput">the script to be opened</param>
		private void openVideo(string videoInputScript, string strSourceFileName, bool inlineAvs)
		{
			this.crop.Checked = false;
            this.input.Filename = "";
            this.originalScript = videoInputScript;
            this.originalInlineAvs = inlineAvs;
            if (player != null)
                player.Dispose();
            bool videoLoaded = showOriginal();
            enableControls(videoLoaded);
            if (videoLoaded)
            {
                eventsOn = false;
                this.input.Filename = strSourceFileName;
                file = player.File;
                reader = player.Reader;
                this.fpsBox.Value = (decimal)file.VideoInfo.FPS;
                if (file.VideoInfo.FPS.Equals(25.0)) // disable ivtc for pal sources
                    this.tvTypeLabel.Text = "PAL";
                else
                    this.tvTypeLabel.Text = "NTSC";
                horizontalResolution.Value = file.VideoInfo.Width;
                verticalResolution.Value = file.VideoInfo.Height;
                if (File.Exists(strSourceFileName))
                {
                    MediaInfoFile oInfo = new MediaInfoFile(strSourceFileName);
                    arChooser.Value = oInfo.VideoInfo.DAR;
                }
                else
                    arChooser.Value = file.VideoInfo.DAR;

                cropLeft.Maximum = cropRight.Maximum = file.VideoInfo.Width / 2;
                cropTop.Maximum = cropBottom.Maximum = file.VideoInfo.Height / 2;
                eventsOn = true;
            }
		}

        private void calcAspectError()
        {
            if (file == null)
            {
                lblAspectError.BackColor = System.Drawing.SystemColors.Window;
                lblAspectError.Text = "0.00000%";
                return;
            }

            // get input dimension with SAR 1:1
            int inputHeight = (int)file.VideoInfo.Height - Cropping.top - Cropping.bottom;
            decimal inputWidth = (int)file.VideoInfo.Width - Cropping.left - Cropping.right;
            if (arChooser.Value.HasValue)
            {
                Sar s = arChooser.Value.Value.ToSar((int)file.VideoInfo.Width, (int)file.VideoInfo.Height);
                inputWidth = inputWidth * s.X / s.Y;
            }

            // get output dimension with SAR 1:1
            int outputHeight = (int)verticalResolution.Value;
            decimal outputWidth = (int)horizontalResolution.Value;
            if (!resize.Checked)
            {
                outputHeight = (int)file.VideoInfo.Height - Cropping.top - Cropping.bottom;
                outputWidth = (int)file.VideoInfo.Width - Cropping.left - Cropping.right;
            }
            if (signalAR.Checked && suggestedDar.HasValue)
            {
                Sar s = suggestedDar.Value.ToSar((int)outputWidth, outputHeight);
                outputWidth = outputWidth * s.X / s.Y;
            }

            if (inputHeight <= 0 || inputWidth <= 0 || outputHeight <= 0 || outputWidth <= 0)
            {
                lblAspectError.BackColor = System.Drawing.SystemColors.Window;
                lblAspectError.Text = "0.00000%";
                return;
            }

            decimal aspectError = (inputHeight * outputWidth) / (inputWidth * outputHeight) - 1;
            lblAspectError.Text = String.Format("{0:0.00000%}", aspectError);
            if (Math.Abs(aspectError) * 100 <= mainForm.Settings.AcceptableAspectErrorPercent)
                lblAspectError.ForeColor = System.Drawing.SystemColors.WindowText;
            else
                lblAspectError.ForeColor = System.Drawing.Color.Red;
        }

		#endregion

		#region updown
		private void changeNumericUpDownColor(NumericUpDown oControl, bool bMarkRed)
        {
            if (oControl.Enabled)
            {
                if (bMarkRed)
                    oControl.ForeColor = System.Drawing.Color.Red;
                else
                    oControl.ForeColor = System.Drawing.SystemColors.WindowText;
                oControl.BackColor = System.Drawing.SystemColors.Window;
            }
            else
            {
                if (bMarkRed)
                    oControl.BackColor = System.Drawing.Color.FromArgb(255, 255, 180, 180);
                else
                    oControl.BackColor = System.Drawing.SystemColors.Window;
                oControl.ForeColor = System.Drawing.SystemColors.WindowText;
            }
        }

		private void sendCropValues()
		{
            if (player == null || !player.Visible)
                return;

            if (isPreviewMode)
                player.crop(0, 0, 0, 0);
            else
                player.crop(Cropping);
		}
		#endregion

		#region checkboxes
		private void deinterlace_CheckedChanged(object sender, EventArgs e)
		{
			if (deinterlace.Checked)
			{
				deinterlaceType.Enabled = true;
				if (deinterlaceType.SelectedIndex == -1)
					deinterlaceType.SelectedIndex = 0; // make sure something is selected
			}
			else
				deinterlaceType.Enabled = false;

            if (sender != null && e != null)
			    showScript(false);
		}

		private void noiseFilter_CheckedChanged(object sender, EventArgs e)
		{
			if (noiseFilter.Checked)
			{
				this.noiseFilterType.Enabled = true;
			}
			else
				this.noiseFilterType.Enabled = false;

            if (sender != null && e != null)
			    showScript(false);
		}

		#endregion

		#region autocrop
		/// <summary>
		/// gets the autocrop values
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void autoCropButton_Click(object sender, EventArgs e)
		{
            if (isPreviewMode || player == null || !player.Visible)
            {
                MessageBox.Show(this, "No AutoCropping without the original video window open",
                    "AutoCropping not possible",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return;
            }

            // don't lock up the GUI, start a new thread
            this.Cursor = System.Windows.Forms.Cursors.WaitCursor;
            System.Threading.Thread t = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                CropValues final = Autocrop.autocrop(reader);
                Invoke(new MethodInvoker(delegate
                {
                    setCropValues(final);
                }));
            }));
            t.IsBackground = true;
            t.Start();
		}

        private void setCropValues(CropValues cropValues)
        {
            this.Cursor = System.Windows.Forms.Cursors.Default;
            bool error = (cropValues.left == -1);
            if (!error)
            {
                eventsOn = false;
                cropLeft.Value = cropValues.left;
                cropTop.Value = cropValues.top;
                cropRight.Value = cropValues.right;
                cropBottom.Value = cropValues.bottom;
                if (!crop.Checked)
                    crop.Checked = true;
                eventsOn = true;
                updateEverything(true, false);
            }
            else
                MessageBox.Show("I'm afraid I was unable to find more than 5 frames that have matching crop values");
        }

		#endregion

        #region properties

        private AviSynthSettings Settings
		{
			set
			{
                eventsOn = false;
				this.resizeFilterType.SelectedItem =  EnumProxy.Create( value.ResizeMethod);
                this.noiseFilterType.SelectedItem = EnumProxy.Create(value.DenoiseMethod);
				this.mpeg2Deblocking.Checked = value.MPEG2Deblock;
				this.colourCorrect.Checked = value.ColourCorrect;
				this.deinterlace.Checked = value.Deinterlace;
				this.noiseFilter.Checked = value.Denoise;
                this.resize.Checked = value.Resize;
                this.mod16Box.SelectedIndex = (int)value.Mod16Method;
                this.signalAR.Checked = (value.Mod16Method != mod16Method.none);
                this.dss2.Checked = value.DSS2;
                this.bAllowUpsizing = value.Upsize;
                if (!bAllowUpsizing && file != null)
                {
                    horizontalResolution.Maximum = file.VideoInfo.Width;
                    verticalResolution.Maximum = file.VideoInfo.Height;
                }
                else
                    horizontalResolution.Maximum = verticalResolution.Maximum = 9999;
                this.modValueBox.SelectedIndex = (int)value.ModValue;
                eventsOn = true;
                updateEverything(true, false);
			}
        }
        
        private CropValues Cropping
        {
            get
            {
                CropValues returnValue = new CropValues();
                if (crop.Checked)
                {
                    returnValue.bottom = (int)cropBottom.Value;
                    returnValue.top = (int)cropTop.Value;
                    returnValue.left = (int)cropLeft.Value;
                    returnValue.right = (int)cropRight.Value;
                    if (Mod16Method == mod16Method.overcrop)
                        ScriptServer.overcrop(ref returnValue, (modValue)modValueBox.SelectedIndex);
                    else if (Mod16Method == mod16Method.mod4Horizontal)
                        ScriptServer.cropMod4Horizontal(ref returnValue);
                    else if (Mod16Method == mod16Method.undercrop)
                        ScriptServer.undercrop(ref returnValue, (modValue)modValueBox.SelectedIndex);
                }
                return returnValue;
            }
        }

        private mod16Method Mod16Method
        {
            get
            {
                mod16Method m = (mod16Method)mod16Box.SelectedIndex;
                if (!mod16Box.Enabled)
                    m = mod16Method.none;
                return m;
            }
        }

        #endregion

        #region autodeint
        private void analyseButton_Click(object sender, EventArgs e)
        {
            if (input.Filename.Length > 0)
            {
                if (detector == null) // We want to start the analysis
                {
                    string source = ScriptServer.GetInputLine(input.Filename, null, false, sourceType, false, false, false, 25, false);
                    if (nvDeInt.Enabled) 
                        source += ")";
                    detector = new SourceDetector(source,
                        input.Filename, deintIsAnime.Checked,
                        mainForm.Settings.SourceDetectorSettings,
                        new UpdateSourceDetectionStatus(analyseUpdate),
                        new FinishedAnalysis(finishedAnalysis));
                        detector.analyse();
                        deintStatusLabel.Text = "Analysing...";
                        analyseButton.Text = "Abort";
                }
                else // We want to cancel the analysis
                {
                    detector.stop();
                    deintStatusLabel.Text = "Analysis aborted!";
                    detector = null;
                    analyseButton.Text = "Analyse";
                    this.deintProgressBar.Value = 0;
                }
            }
            else
                MessageBox.Show("Can't run any analysis as there is no selected video to analyse.",
                    "Please select a video input file", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public void finishedAnalysis(SourceInfo info, bool error, string errorMessage)
        {
            if (error)
            {
                detector.stop();
                Invoke(new MethodInvoker(delegate
                {
                    MessageBox.Show(this, errorMessage, "Error in analysis", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    deintStatusLabel.Text = "Analysis failed!";
                    analyseButton.Text = "Analyse";
                    this.deintProgressBar.Value = 0;
                }));
            }
            else
            {
                try
                {
                    info.isAnime = deintIsAnime.Checked;
                    Invoke(new MethodInvoker(delegate
                    {
                        deintProgressBar.Enabled = false;
                        this.DeintInfo = info;
                        if (deintSourceType.SelectedIndex < 1)
                        {
                            deinterlace.Enabled = false;
                            deinterlace.Checked = false;
                        }
                        else
                            deinterlace.Enabled = true;
                        if (deinterlaceType.Text == "Do nothing")
                            deinterlace.Checked = false;
                        else
                            deinterlace.Checked = true;
                        deintStatusLabel.Text = "Analysis finished!";
                        analyseButton.Text = "Analyse";
                    }));
                }
                catch (Exception) { } // If we get any errors, it's most likely because the window was closed, so just ignore
            }
            detector = null;
        }

        public void analyseUpdate(int amountDone, int total)
        {
            try
            {
                Invoke(new MethodInvoker(delegate
                    {
                        this.deintProgressBar.Value = amountDone;
                        this.deintProgressBar.Maximum = total;
                    }));
            }
            catch (Exception) { } // If we get any errors, just ignore -- it's only a cosmetic thing.
        }
        #endregion

        private void deintSourceType_SelectedIndexChanged(object sender, EventArgs e)
        {
            deintM.Enabled = (deintSourceType.SelectedItem == ScriptServer.ListOfSourceTypes[(int)UserSourceType.Decimating]);
            deintFieldOrder.Enabled = !(deintSourceType.SelectedItem == ScriptServer.ListOfSourceTypes[(int)UserSourceType.Progressive]);
            deinterlaceType.DataSource = ScriptServer.GetDeinterlacers(DeintInfo);
            deinterlaceType.BindingContext = new BindingContext();
            if (deintSourceType.SelectedIndex < 1)
            {
                deinterlace.Enabled = false;
                deinterlace.Checked = false;
            }
            else
                deinterlace.Enabled = true;

            if (sender != null && e != null)
                showScript(false);
        }

        private void reopenOriginal_Click(object sender, EventArgs e)
        {
            reopenOriginal.Enabled = false;
            reopenOriginal.Text = "Please wait...";
            if (chAutoPreview.Checked)
                chAutoPreview.Checked = false;
            else
                showOriginal();
            reopenOriginal.Enabled = true;
            reopenOriginal.Text = "Re-open original video player";
        }

        private void chAutoPreview_CheckedChanged(object sender, EventArgs e)
        {
            if (chAutoPreview.Checked)
                previewButton_Click(null, null);
            else if (this.isPreviewMode == true)
                showOriginal();
        }

        private void nvDeInt_CheckedChanged(object sender, EventArgs e)
        {
            if (nvDeInt.Checked)
                cbNvDeInt.Enabled = true;
            else 
                cbNvDeInt.Enabled = false;
            if (sender != null && e != null)
                showScript(false);
        }

        private void nvDeInt_Click(object sender, EventArgs e)
        {
            // just to be sure
            checkNVCompatibleFile(input.Filename);
        }

        private void openSubtitlesButton_Click(object sender, EventArgs e)
        {
            if (this.openSubsDialog.ShowDialog() != DialogResult.OK)
                return;

            if (this.SubtitlesPath.Text != openSubsDialog.FileName)
            {
                string ext = Path.GetExtension(openSubsDialog.FileName).ToString().ToLower(System.Globalization.CultureInfo.InvariantCulture);
                this.SubtitlesPath.Text = openSubsDialog.FileName;
                if (ext == ".idx")
                    cbCharset.Enabled = false;
                MessageBox.Show("Subtitles successfully added to the script...", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else 
                MessageBox.Show("The subtitles you chosen was already added...", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (sender != null && e != null)
                showScript(false);
        }

        private string CharsetValue()
        {
            string c = string.Empty;

            if (!string.IsNullOrEmpty(SubtitlesPath.Text))
            {
                switch (cbCharset.SelectedIndex)
                {
                    case 1: c = "0"; break;
                    case 2: c = "2"; break;
                    case 3: c = "128"; break;
                    case 4:
                    case 5: c = "129"; break;
                    case 6: c = "134"; break;
                    case 7: c = "136"; break;
                    case 8: c = "255"; break;
                    case 9: c = "130"; break;
                    case 10: c = "177"; break;
                    case 11: c = "178"; break;
                    case 12: c = "161"; break;
                    case 13: c = "162"; break;
                    case 14: c = "163"; break;
                    case 15: c = "222"; break;
                    case 16: c = "238"; break;
                    case 17: c = "204"; break;
                    case 18: c = "77"; break;
                    case 19: c = "186"; break;
                    default: c = "1"; break;
                }
            }
            return c;
        }

        private void inputDARChanged(object sender, string val)
        {
            updateEverything(sender != null, false);
        }

        private void updateEverything(object sender, EventArgs e)
        {
            updateEverything(sender != null, false);
        }

        private void updateEverything(bool bShowScript, bool bForceScript)
        {
            if (!eventsOn)
                return;
            eventsOn = false;

            // update events may be triggered
            setModType();
            setCrop();
            setOutputResolution();

            // no update events triggered
            calcAspectError();
            checkControls();
            
            eventsOn = true;
            if (bShowScript)
                showScript(bForceScript);
        }

        private void setModType()
        {
            if (!bAllowUpsizing && file != null)
            {
                horizontalResolution.Maximum = file.VideoInfo.Width;
                verticalResolution.Maximum = file.VideoInfo.Height;
            }
            else
                horizontalResolution.Maximum = verticalResolution.Maximum = 9999;

            if (signalAR.Checked)
            {
                mod16Box.Enabled = true;
                if (mod16Box.SelectedIndex == -1)
                    mod16Box.SelectedIndex = 0;
            }
            else
                mod16Box.Enabled = false;

            if (Mod16Method == mod16Method.overcrop)
                crop.Text = "Crop (will be rounded up to selected mod)";
            else if (Mod16Method == mod16Method.undercrop)
                crop.Text = "Crop (will be rounded down to sel. mod)";
            else
                crop.Text = "Crop";

            if (Mod16Method == mod16Method.resize)
            {
                resize.Enabled = false;
                resize.Checked = true;
                suggestResolution.Enabled = false;
                suggestResolution.Checked = true;
            }
            else if (Mod16Method == mod16Method.none)
            {
                resize.Enabled = true;
                suggestResolution.Enabled = resize.Checked;
                if (!suggestResolution.Enabled)
                    suggestResolution.Checked = true;
            }
            else
            {
                resize.Checked = false;
                resize.Enabled = false;
                suggestResolution.Enabled = false;
                suggestResolution.Checked = true;
            }

            if (resize.Checked || (signalAR.Checked && (Mod16Method == mod16Method.resize || Mod16Method == mod16Method.overcrop || Mod16Method == mod16Method.undercrop)))
                modValueBox.Enabled = true;
            else
                modValueBox.Enabled = false;
        }

        private void setCrop()
        {
            if (crop.Checked)
            {
                this.cropLeft.Enabled = true;
                this.cropTop.Enabled = true;
                this.cropRight.Enabled = true;
                this.cropBottom.Enabled = true;
                sendCropValues();

                if (file != null)
                {
                    int inputHeight = (int)file.VideoInfo.Height - Cropping.top - Cropping.bottom;
                    int inputWidth = (int)file.VideoInfo.Width - Cropping.left - Cropping.right;
                    if (!resize.Checked)
                    {
                        verticalResolution.Value = inputHeight;
                        horizontalResolution.Value = inputWidth;
                    }
                    if (!bAllowUpsizing)
                    {
                        verticalResolution.Maximum = inputHeight;
                        horizontalResolution.Maximum = inputWidth;
                    }
                }
            }
            else
            {
                this.cropLeft.Enabled = false;
                this.cropTop.Enabled = false;
                this.cropRight.Enabled = false;
                this.cropBottom.Enabled = false;
                if (player != null && player.Visible)
                    player.crop(0, 0, 0, 0);
            }
        }

        private void setOutputResolution()
        {
            if (resize.Checked)
            {
                this.horizontalResolution.Enabled = true;
                this.verticalResolution.Enabled = !suggestResolution.Checked;
            }
            else
                this.horizontalResolution.Enabled = this.verticalResolution.Enabled = false;

            if (file == null)
                return;

            try
            {
                double dar = 1.0;
                dar = (double)arChooser.RealValue.ar;
                Dar? suggestedDar;

                int mod = 16;
                switch ((modValue)modValueBox.SelectedIndex)
                {
                    case modValue.mod8: mod = 8; break;
                    case modValue.mod4: mod = 4; break;
                    case modValue.mod2: mod = 2; break;
                }
                horizontalResolution.Increment = verticalResolution.Increment = mod;

                int hres = (int)horizontalResolution.Value;

                // remove upsizing if not allowed
                if (!bAllowUpsizing && (int)file.VideoInfo.Width - Cropping.left - Cropping.right < hres)
                    hres = (int)file.VideoInfo.Width - Cropping.left - Cropping.right;
                else if (!horizontalResolution.Enabled) // remove upsizing or undersizing if value cannot be changed
                    hres = (int)file.VideoInfo.Width - Cropping.left - Cropping.right;

                // correct hres if not mod compliant and resize is enabled
                if (resize.Checked && hres % mod != 0)
                {
                    int diff = hres % mod;
                    if (hres - diff > 0)
                        hres -= diff;
                    else
                        hres += mod - diff;
                }

                if (hres != horizontalResolution.Value)
                    horizontalResolution.Value = hres;

                bool signalAR = this.signalAR.Checked;
                int scriptVerticalResolution = Resolution.suggestResolution((int)file.VideoInfo.Height, (int)file.VideoInfo.Width, dar, Cropping,
                    (int)horizontalResolution.Value, signalAR, out suggestedDar, mod);

                if (suggestResolution.Checked)
                {
                    this.verticalResolution.Enabled = false;
                    if (scriptVerticalResolution > verticalResolution.Maximum)
                    {
                        // Reduce horizontal resolution until a fit is found that doesn't require upsizing. This is really only needed for oddball DAR scenarios
                        hres = (int)horizontalResolution.Value;
                        do
                        {
                            hres -= mod;
                            scriptVerticalResolution = Resolution.suggestResolution((int)file.VideoInfo.Height, (int)file.VideoInfo.Width, dar, Cropping,
                                hres, signalAR, out suggestedDar, mod);
                        }
                        while (scriptVerticalResolution > verticalResolution.Maximum && hres > 0);
                        horizontalResolution.Value = hres;
                    }
                    verticalResolution.Value = (decimal)scriptVerticalResolution;
                }
                else
                {
                    this.verticalResolution.Enabled = resize.Checked;
                    if (!verticalResolution.Enabled)
                        verticalResolution.Value = (int)file.VideoInfo.Height - Cropping.top - Cropping.bottom;
                }

                if (signalAR)
                    this.suggestedDar = suggestedDar;
            }
            catch (Exception exc)
            {
                MessageBox.Show("Error in computing resolution\r\n" + exc.Message, "Unspecified Error", MessageBoxButtons.OK);
            }
        }

        private void checkControls()
        {
            if (resize.Checked && file != null && (int)file.VideoInfo.Height - Cropping.top - Cropping.bottom < verticalResolution.Value)
                changeNumericUpDownColor(verticalResolution, true);
            else
                changeNumericUpDownColor(verticalResolution, false);

            if (resize.Checked && file != null && (int)file.VideoInfo.Width - Cropping.left - Cropping.right < horizontalResolution.Value)
                changeNumericUpDownColor(horizontalResolution, true);
            else
                changeNumericUpDownColor(horizontalResolution, false);
        }

        private void refreshScript(object sender, EventArgs e)
        {
            if (sender != null && e != null)
                showScript(false);
        }

        private void AviSynthWindow_Shown(object sender, EventArgs e)
        {
            if (player != null && !player.Visible)
                player.Show();
        }
    }
    public delegate void OpenScriptCallback(string avisynthScript);
    public enum PossibleSources { d2v, dga, dgi, mpeg2, vdr, directShow, avs, ffindex };
    public enum mod16Method : int { none = -1, resize = 0, overcrop, nonMod16, mod4Horizontal, undercrop };
    public enum modValue : int { mod16 = 0, mod8, mod4, mod2 };

    public class AviSynthWindowTool : MeGUI.core.plugins.interfaces.ITool
    {

        #region ITool Members

        public string Name
        {
            get { return "AVS Script Creator"; }
        }

        public void Run(MainForm info)
        {
            info.ClosePlayer();
            AviSynthWindow asw = new AviSynthWindow(info);
            asw.OpenScript += new OpenScriptCallback(info.Video.openVideoFile);
            asw.Show();
        }

        public Shortcut[] Shortcuts
        {
            get { return new Shortcut[] { Shortcut.CtrlR }; }
        }

        #endregion

        #region IIDable Members

        public string ID
        {
            get { return "AvsCreator"; }
        }

        #endregion
    }
}
