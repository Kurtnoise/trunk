// ****************************************************************************
// 
// Copyright (C) 2005-2009  Doom9 & al
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

using Utils.MessageBoxExLib;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

using MeGUI.core.details;
using MeGUI.core.gui;
using MeGUI.core.util;

using MediaInfoWrapper;

namespace MeGUI
{
	/// <summary>
	/// VideoUtil is used to perform various video related tasks, namely autocropping, 
	/// auto resizing
	/// </summary>
	public class VideoUtil
    {

        private MainForm mainForm;
		private JobUtil jobUtil;
		public VideoUtil(MainForm mainForm)
		{
			this.mainForm = mainForm;
			jobUtil = new JobUtil(mainForm);
        }

		#region finding source information
		
        /// <summary>
		/// gets the dvd decrypter generated chapter file
		/// </summary>
		/// <param name="fileName">name of the first vob to be loaded</param>
		/// <returns>full name of the chapter file or an empty string if no file was found</returns>
		public static string getChapterFile(string fileName)
		{
            string vts;
			string path = Path.GetDirectoryName(fileName);
			string name = Path.GetFileNameWithoutExtension(fileName);
            if (name.Length > 6)
                vts = name.Substring(0, 6);
            else
                vts = name;
			string chapterFile = "";
            string[] files = Directory.GetFiles(path, vts + "*Chapter Information*");
			foreach (string file in files)
			{
				if (file.ToLower().EndsWith(".txt") || file.ToLower().EndsWith(".qpf"))
                {
					chapterFile = file;
					break;
				}                   
			}
			return chapterFile;
		}
		
        /// <summary>
        /// gets information about a video source using MediaInfo
        /// </summary>
        /// <param name="infoFile">the info file to be analyzed</param>
        /// <param name="audioTracks">the audio tracks found</param>
        /// <param name="maxHorizontalResolution">the width of the video</param>
        public void getSourceMediaInfo(string fileName, out List<AudioTrackInfo> audioTracks, out int maxHorizontalResolution, out Dar? dar)
        {
            MediaInfo info;
            audioTracks = new List<AudioTrackInfo>();
            maxHorizontalResolution = 5000;
            dar = Dar.A1x1;
            try
            {
                info = new MediaInfo(fileName);
                maxHorizontalResolution = Int32.Parse(info.Video[0].Width);

                if (info.Video[0].Width == "720" && (info.Video[0].Height == "576" || info.Video[0].Height == "480"))
                {
                    if (info.Video[0].Height == "576")
                    {
                        if (info.Video[0].AspectRatioString.Equals("16:9"))
                            dar = Dar.ITU16x9PAL;
                        else if (info.Video[0].AspectRatioString.Equals("4:3"))
                            dar = Dar.ITU4x3PAL;
                        else
                            dar = new Dar(ulong.Parse(info.Video[0].Width), ulong.Parse(info.Video[0].Height));
                    }
                    else
                    {
                        if (info.Video[0].AspectRatioString.Equals("16:9"))
                            dar = Dar.ITU16x9NTSC;
                        else if (info.Video[0].AspectRatioString.Equals("4:3"))
                            dar = Dar.ITU4x3NTSC;
                        else
                            dar = new Dar(ulong.Parse(info.Video[0].Width), ulong.Parse(info.Video[0].Height));
                    }
                }
                else
                {
                    dar = new Dar(ulong.Parse(info.Video[0].Width), ulong.Parse(info.Video[0].Height));
                }

                for (int counter = 0; counter < info.Audio.Count; counter++)
                {
                    MediaInfoWrapper.AudioTrack atrack = info.Audio[counter];
                    AudioTrackInfo ati = new AudioTrackInfo();
                    // DGIndex expects audio index not ID for TS
                    ati.ContainerType = info.General[0].Format;
                    ati.Index = counter;
                    if (info.General[0].Format == "CDXA/MPEG-PS")
                        // MediaInfo doesn't give TrackID for VCD, specs indicate only MP1L2 is supported
                        ati.TrackID = (0xC0 + counter);
                    else if (atrack.ID != "0" && atrack.ID != "")
                        ati.TrackID = Int32.Parse(atrack.ID);
                    else
                        // MediaInfo failed to get ID try guessing based on codec
                        switch (atrack.Format.Substring(0,3))
                        {
                            case "AC3": ati.TrackID = (0x80 + counter); break;
                            case "PCM": ati.TrackID = (0xA0 + counter); break;
                            case "MPE": // MPEG-1 Layer 1/2/3
                            case "MPA": ati.TrackID = (0xC0 + counter); break;
                            case "DTS": ati.TrackID = (0x88 + counter); break;
                        }
                    if (atrack.FormatProfile != "") // some tunings to have a more useful info instead of a typical audio Format
                    {
                        switch (atrack.FormatProfile)
                        {   
                            case "Dolby Digital": ati.Type = "AC-3"; break;
                            case "HRA": ati.Type = "DTS-HD High Resolution"; break;
                            case "Layer 1": ati.Type = "MPA"; break;
                            case "Layer 2": ati.Type = "MP2"; break;
                            case "Layer 3": ati.Type = "MP3"; break;
                            case "LC": ati.Type = "AAC"; break;
                            case "MA": ati.Type = "DTS-HD Master Audio"; break;
                            case "TrueHD": ati.Type = "TrueHD"; break;
                        }
                    }
                    else ati.Type = atrack.Format;
                    ati.NbChannels = atrack.ChannelsString;
                    ati.SamplingRate = atrack.SamplingRateString;
                    if (atrack.LanguageString == "") // to retrieve Language 
                    {
                        if (Path.GetExtension(fileName.ToLower()) == ".vob")
                        {
                            string ifoFile;
                            string fileNameNoPath = Path.GetFileName(fileName);

                            // Languages are not present in VOB, so we check the main IFO
                            if (fileNameNoPath.Substring(0, 4) == "VTS_")
                                 ifoFile = fileName.Substring(0, fileName.LastIndexOf("_")) + "_0.IFO";
                            else ifoFile = Path.ChangeExtension(fileName, ".IFO");

                            if (File.Exists(ifoFile))
                                atrack.LanguageString = IFOparser.getAudioLanguage(ifoFile, counter);
                        }
                    }
                    ati.TrackInfo = new TrackInfo(atrack.LanguageString, null);
                    audioTracks.Add(ati);
                    if (info.General[0].Format == "MPEG-TS")
                        break;  // DGIndex only supports first audio stream with TS files
                }
            }
            catch (Exception i)
            {
                MessageBox.Show("The following error ocurred when trying to get Media info for file " + fileName + "\r\n" + i.Message, "Error parsing mediainfo data", MessageBoxButtons.OK);
                audioTracks.Clear();
            }
        }

        /// <summary>
        /// gets chapters from IFO file and save them as Ogg Text File
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns>chapter file name</returns>
        public static String getChaptersFromIFO(string fileName, bool qpfile)
        {
            if (Path.GetExtension(fileName.ToLower()) == ".vob")
            {
                string ifoFile;
                string fileNameNoPath = Path.GetFileName(fileName);

                // we check the main IFO
                if (fileNameNoPath.Substring(0, 4) == "VTS_")
                    ifoFile = fileName.Substring(0, fileName.LastIndexOf("_")) + "_0.IFO";
                else 
                    ifoFile = Path.ChangeExtension(fileName, ".IFO");

                if (File.Exists(ifoFile))
                {
                    ChapterInfo pgc;
                    ChapterExtractor ex = new IfoExtractor();
                    pgc = ex.GetStreams(ifoFile)[0];
                    if (Drives.ableToWriteOnThisDrive(Path.GetPathRoot(ifoFile)))
                    {
                        if (qpfile)
                            pgc.SaveQpfile(Path.GetDirectoryName(ifoFile) + "\\" + fileNameNoPath.Substring(0, 6) + " - Chapter Information.qpf");

                        // save always this format - some users want it for the mux
                        pgc.SaveText(Path.GetDirectoryName(ifoFile) + "\\" + fileNameNoPath.Substring(0, 6) + " - Chapter Information.txt");
                        return Path.GetDirectoryName(ifoFile) + "\\" + fileNameNoPath.Substring(0, 6) + " - Chapter Information.txt";
                    }
                    else
                        MessageBox.Show("MeGUI cannot write on the disc " + Path.GetPathRoot(ifoFile) + " \n" +
                                        "Please, select another output path to save the chapters file...", "Configuration Incomplete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            return null;
        }

        /// <summary>
        /// gets Timeline from Chapters Text file (formally as Ogg Format)
        /// </summary>
        /// <param name="fileName">the file read</param>
        /// <returns>chapters Timeline as string</returns>
        public static string getChapterTimeLine(string fileName)
        {
            long count = 0;
            string line;
            string chap = "=";
                
            using (StreamReader r = new StreamReader(fileName))
            {
                while ((line = r.ReadLine()) != null)
                {
                    count++;
                    if (count % 2 != 0) // odd line
                    {
                        if (count >= 2)
                            chap += ";";
                        chap += line.Substring(line.IndexOf("=") + 1, 12);
                    }
                }
            }
            return chap;
        }

        /// <summary>
        /// gets ID from a first video stream using MediaInfo
        /// </summary>
        /// <param name="infoFile">the file to be analyzed</param>
        /// <returns>the video track ID found</returns>
        public static int getIDFromFirstVideoStream(string fileName)
        {
            MediaInfo info;
            int TrackID = 0;
            try
            {
                info = new MediaInfo(fileName);
                if (info.Video.Count > 0)
                {
                    MediaInfoWrapper.VideoTrack vtrack = info.Video[0];
                    TrackID = Int32.Parse(vtrack.ID);
                }
            }
            catch (Exception i)
            {
                MessageBox.Show("The following error ocurred when trying to get Media info for file " + fileName + "\r\n" + i.Message, "Error parsing mediainfo data", MessageBoxButtons.OK);
            }
            return TrackID;
        }

        /// detect AVC stream from a file using MediaInfo
        /// </summary>
        /// <param name="infoFile">the file to be analyzed</param>
        /// <returns>AVC stream found whether or not</returns>
        public static bool detecAVCStreamFromFile(string fileName)
        {
            MediaInfo info;
            bool avcS = false;
            try
            {
                info = new MediaInfo(fileName);
                if (info.Video.Count > 0)
                {
                    MediaInfoWrapper.VideoTrack vtrack = info.Video[0];
                    string format = vtrack.Format;
                    if (format == "AVC")
                        avcS = true;
                }
            }
            catch (Exception i)
            {
                MessageBox.Show("The following error ocurred when trying to get Media info for file " + fileName + "\r\n" + i.Message, "Error parsing mediainfo data", MessageBoxButtons.OK);
            }
            return avcS;
        }

        public static List<string> setDeviceTypes(string outputFormat)
        {
            List<string> deviceList = new List<string>();
            switch (outputFormat)
            {
                case ".avi": deviceList.AddRange(new string[] { "PC" }); break;
                case ".mp4": deviceList.AddRange(new string[] { "iPhone", "iPod", "ISMA", "PSP" }); break;
                case ".m2ts": deviceList.AddRange(new string[] { "AVCHD", "Blu-ray" }); break;
            }

            return deviceList;
        }

 		#endregion

		#region dgindex postprocessing
		/// <summary>
		/// gets all demuxed audio files from a given dgindex project
		/// starts with the first file and returns the desired number of files
		/// </summary>
        /// <param name="audioTrackIDs">list of audio TrackIDs</param>
		/// <param name="projectName">the name of the dgindex project</param>
		/// <param name="cutoff">maximum number of results to be returned</param>
		/// <returns>an array of string of filenames</returns>
        public Dictionary<int, string> getAllDemuxedAudio(List<AudioTrackInfo> audioTracks, out List<string> arrDeleteFiles, string projectName, LogItem log)
        {
		    Dictionary<int, string> audioFiles = new Dictionary<int, string>();
            arrDeleteFiles = new List<string>();
            string strTrackName;
            string[] files;

            if (audioTracks == null || audioTracks.Count == 0)
                return audioFiles;

            if (audioTracks[0].ContainerType.ToLower().Equals("matroska"))
                strTrackName = " [";
            else if (audioTracks[0].ContainerType == "MPEG-TS" || audioTracks[0].ContainerType == "BDAV")
                strTrackName = " PID ";
            else
                strTrackName = " T";

            for (int counter = 0; counter < audioTracks.Count; counter++)
            {
                bool bFound = false;
                string trackFile = strTrackName + audioTracks[counter].TrackIDx + "*";
                if (Path.GetExtension(projectName).ToLower().Equals(".dga"))
                    trackFile = Path.GetFileName(projectName) + trackFile;
                else if (Path.GetExtension(projectName).ToLower().Equals(".ffindex"))
                    trackFile = Path.GetFileNameWithoutExtension(projectName) + "_track_" + (audioTracks[counter].Index + 1) + "_*.avs";
                else
                    trackFile = Path.GetFileNameWithoutExtension(projectName) + trackFile;
                    
                files = Directory.GetFiles(Path.GetDirectoryName(projectName), trackFile);
                foreach (string file in files)
                {
                    if ( file.EndsWith(".ac3") ||
                         file.EndsWith(".mp3") ||
                         file.EndsWith(".mp2") ||
                         file.EndsWith(".mp1") ||
                         file.EndsWith(".mpa") ||
                         file.EndsWith(".dts") ||
                         file.EndsWith(".wav") ||
                         file.EndsWith(".ogg") ||
                         file.EndsWith(".flac") ||
                         file.EndsWith(".ra") ||
                         file.EndsWith(".avs") ||
                         file.EndsWith(".aac")) // It is the right track
					{
                        bFound = true;
                        if (!audioFiles.ContainsValue(file))
                            audioFiles.Add(audioTracks[counter].TrackID, file);
                        break;
					}
				}
                if (!bFound && log != null)
                    log.LogEvent("File not found: " + Path.Combine(Path.GetDirectoryName(projectName), trackFile), ImageType.Error);
			}

            // Find files which can be deleted
            if (Path.GetExtension(projectName).ToLower().Equals(".dga"))
                strTrackName = Path.GetFileName(projectName) + strTrackName;
            else
                strTrackName = Path.GetFileNameWithoutExtension(projectName) + strTrackName;

            files = Directory.GetFiles(Path.GetDirectoryName(projectName), strTrackName + "*");
            foreach (string file in files)
            {
                if (file.EndsWith(".ac3") ||
                     file.EndsWith(".mp3") ||
                     file.EndsWith(".mp2") ||
                     file.EndsWith(".mp1") ||
                     file.EndsWith(".mpa") ||
                     file.EndsWith(".dts") ||
                     file.EndsWith(".wav") ||
                     file.EndsWith(".avs") ||
                     file.EndsWith(".aac")) // It is the right track
                {
                    if (!audioFiles.ContainsValue(file))
                        arrDeleteFiles.Add(file);
                }
            }
            return audioFiles;
		}

        public Dictionary<int, string> getAllDemuxedSubtitles(List<SubtitleInfo> subTracks, string projectName)
        {
            Dictionary<int, string> subFiles = new Dictionary<int, string>();
            for (int counter = 0; counter < subTracks.Count; counter++)
            {
                string[] files = Directory.GetFiles(Path.GetDirectoryName(projectName),
                        Path.GetFileNameWithoutExtension(projectName));
                foreach (string file in files)
                {
                    if (file.EndsWith(".idx") ||
                        file.EndsWith(".srt") ||
                        file.EndsWith(".ssa") ||
                        file.EndsWith(".ass")) // It is the right track
                    {
                        subFiles.Add(subTracks[counter].Index, file);
                        break;
                    }
                }
            }
            return subFiles;
        }

		#endregion

		#region automated job generation
		/// <summary>
		/// ensures that video and audio don't have the same filenames which would lead to severe problems
		/// </summary>
		/// <param name="videoOutput">name of the encoded video file</param>
		/// <param name="muxedOutput">name of the final output</param>
		/// <param name="aStreams">all encodable audio streams</param>
		/// <param name="audio">all muxable audio streams</param>
		/// <returns>the info to be added to the log</returns>
		public LogItem eliminatedDuplicateFilenames(ref string videoOutput, ref string muxedOutput, AudioJob[] aStreams)
		{
            LogItem log = new LogItem("Eliminating duplicate filenames");
            videoOutput = Path.GetFullPath(videoOutput);
            muxedOutput = Path.GetFullPath(muxedOutput);

            log.LogValue("Video output file", videoOutput);
            if (File.Exists(videoOutput))
            {
                int counter = 0;
                string directoryname = Path.GetDirectoryName(videoOutput);
                string filename = Path.GetFileNameWithoutExtension(videoOutput);
                string extension = Path.GetExtension(videoOutput);

                while (File.Exists(videoOutput))
                {
                    videoOutput = Path.Combine(directoryname,
                        filename + "_" + counter + extension);
                    counter++;
                }

                log.LogValue("File already exists. New video output filename", videoOutput);
            }

            log.LogValue("Muxed output file", muxedOutput);
            if (File.Exists(muxedOutput) || muxedOutput == videoOutput)
            {
                int counter = 0;
                string directoryname = Path.GetDirectoryName(muxedOutput);
                string filename = Path.GetFileNameWithoutExtension(muxedOutput);
                string extension = Path.GetExtension(muxedOutput);

                while (File.Exists(muxedOutput) || muxedOutput == videoOutput)
                {
                    muxedOutput = Path.Combine(directoryname,
                        filename + "_" + counter + extension);
                    counter++;
                }

                log.LogValue("File already exists. New muxed output filename", muxedOutput);
            }

			for (int i = 0; i < aStreams.Length; i++)
			{
				string name = Path.GetFullPath(aStreams[i].Output);
                log.LogValue("Encodable audio stream " + i, name);
				if (name.Equals(videoOutput) || name.Equals(muxedOutput)) // audio will be overwritten -> no good
				{
					name = Path.Combine(Path.GetDirectoryName(name), Path.GetFileNameWithoutExtension(name) + i.ToString() + Path.GetExtension(name));
					aStreams[i].Output = name;
                    log.LogValue("Stream has the same name as video stream. New audio stream output", name);
				}
			}
            return log;

		}
        #endregion

        #region source checking
        public string checkVideo(string avsFile)
        {
            return checkVideo(avsFile, true);
        }
        
        private string checkVideo(string avsFile, bool tryToFix)
        {
            try
            {
                using (AvsFile avi = AvsFile.OpenScriptFile(avsFile))
                {
                    if (avi.Clip.OriginalColorspace != AviSynthColorspace.YV12 && avi.Clip.OriginalColorspace != AviSynthColorspace.I420)
                    {
                        if (tryToFix && !isConvertedToYV12(avsFile))
                        {
                            bool convert = mainForm.DialogManager.addConvertToYV12(avi.Clip.OriginalColorspace.ToString());
                            if (convert)
                            {
                                if (appendConvertToYV12(avsFile))
                                {
                                    string sResult = checkVideo(avsFile, false); // Check everything again, to see if it is all fixed now
                                    if (sResult == null)
                                    {
                                        MessageBox.Show("Successfully converted to YV12.");
                                        return null;
                                    }
                                    else
                                    {
                                        return sResult;
                                    }
                                }
                            }
                            return "You didn't want me to append ConvertToYV12(). You'll have to fix the colorspace problem yourself.";
                        }
                        return string.Format("AviSynth clip is in {0} not in YV12, even though ConvertToYV12() has been appended.", avi.Clip.OriginalColorspace.ToString());
                    }

                    VideoCodecSettings settings = GetCurrentVideoSettings();

                    if (settings != null && settings.SettingsID != "x264") // mod16 restriction
                    {
                        if (avi.Clip.VideoHeight % 16 != 0 ||
                            avi.Clip.VideoWidth % 16 != 0)
                            return string.Format("AviSynth clip doesn't have mod16 dimensions:\r\nWidth: {0}\r\nHeight:{1}\r\n" +
                                "This could cause problems with some encoders,\r\n" +
                                "and will also result in a loss of compressibility.\r\n" +
                                "I suggest you resize to a mod16 resolution.", avi.Clip.VideoWidth, avi.Clip.VideoHeight);
                    }
                }
            }
            catch (Exception e)
            {
                return "Error in AviSynth script:\r\n" + e.Message;
            }
            return null;
        }

        private bool appendConvertToYV12(string file)
        {
            try
            {
                StreamWriter avsOut = new StreamWriter(file, true);
                avsOut.Write("\r\nConvertToYV12()");
                avsOut.Close();
            }
            catch (IOException)
            {
                return false; 
            }
            return true;
        }

        private bool isConvertedToYV12(string file)
        {
            try
            {
                String strLastLine = "", line = "";
                using (StreamReader reader = new StreamReader(file))
                {
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!String.IsNullOrEmpty(line))
                            strLastLine = line;
                    }
                }
                if (strLastLine.ToLower().Equals("converttoyv12()"))
                    return true;
                else
                    return false;
            }
            catch
            {
                return false;
            }
        }

        delegate VideoCodecSettings CurrentSettingsDelegate();
        private VideoCodecSettings GetCurrentVideoSettings()
        {
            if (mainForm.InvokeRequired)
                return (VideoCodecSettings)mainForm.Invoke(new CurrentSettingsDelegate(GetCurrentVideoSettings));
            else
                return mainForm.Video.CurrentSettings;
        }
        #endregion

        #region new stuff
        public JobChain GenerateJobSeries(VideoStream video, string muxedOutput, AudioJob[] audioStreams,
            MuxStream[] subtitles, string chapters, FileSize? desiredSize, FileSize? splitSize, ContainerType container, bool prerender, MuxStream[] muxOnlyAudio, LogItem log, string deviceType, Zone[] zones)
        {
            if (desiredSize.HasValue)
            {
                if (video.Settings.EncodingMode != 4 && video.Settings.EncodingMode != 8) // no automated 2/3 pass
                {
                    if (this.mainForm.Settings.NbPasses == 2)
                        video.Settings.EncodingMode = 4; // automated 2 pass
                    else if (video.Settings.MaxNumberOfPasses == 3)
                        video.Settings.EncodingMode = 8;
                }
            }

            fixFileNameExtensions(video, audioStreams, container);
            string videoOutput = video.Output;
            log.Add(eliminatedDuplicateFilenames(ref videoOutput, ref muxedOutput, audioStreams));
            video.Output = videoOutput;

            JobChain vjobs = jobUtil.prepareVideoJob(video.Input, video.Output, video.Settings, video.DAR, prerender, true, zones);

            if (vjobs == null) return null;
            /* Here, we guess the types of the files based on extension.
             * This is guaranteed to work with MeGUI-encoded files, because
             * the extension will always be recognised. For non-MeGUI files,
             * we can only ever hope.*/
            List<MuxStream> allAudioToMux = new List<MuxStream>();
            List<MuxableType> allInputAudioTypes = new List<MuxableType>();
            foreach (MuxStream muxStream in muxOnlyAudio)
            {
                if (VideoUtil.guessAudioMuxableType(muxStream.path, true) != null)
                {
                    allInputAudioTypes.Add(VideoUtil.guessAudioMuxableType(muxStream.path, true));
                    allAudioToMux.Add(muxStream);
                }
            }

            foreach (AudioJob stream in audioStreams)
            {
                allAudioToMux.Add(stream.ToMuxStream());
                allInputAudioTypes.Add(stream.ToMuxableType());
            }

            List<MuxableType> allInputSubtitleTypes = new List<MuxableType>();
            foreach (MuxStream muxStream in subtitles)
                if (VideoUtil.guessSubtitleType(muxStream.path) != null)
                    allInputSubtitleTypes.Add(new MuxableType(VideoUtil.guessSubtitleType(muxStream.path), null));

            MuxableType chapterInputType = null;
            if (!String.IsNullOrEmpty(chapters))
            {
                ChapterType type = VideoUtil.guessChapterType(chapters);
                if (type != null)
                    chapterInputType = new MuxableType(type, null);
            }

            MuxableType deviceOutputType = null;
            if (!String.IsNullOrEmpty(deviceType))
            {
                DeviceType type = VideoUtil.guessDeviceType(deviceType);
                if (type != null)
                    deviceOutputType = new MuxableType(type, null);
            }

            List<string> inputsToDelete = new List<string>();
            inputsToDelete.Add(video.Output);
            inputsToDelete.AddRange(Array.ConvertAll<AudioJob, string>(audioStreams, delegate(AudioJob a) { return a.Output; }));

            JobChain muxJobs = this.jobUtil.GenerateMuxJobs(video, video.Framerate, allAudioToMux.ToArray(), allInputAudioTypes.ToArray(),
                subtitles, allInputSubtitleTypes.ToArray(), chapters, chapterInputType, container, muxedOutput, splitSize, inputsToDelete, deviceType, deviceOutputType);

            if (desiredSize.HasValue)
            {
                BitrateCalculationInfo b = new BitrateCalculationInfo();
                
                List<string> audiofiles = new List<string>();
                foreach (MuxStream s in allAudioToMux)
                    audiofiles.Add(s.path);
                b.AudioFiles = audiofiles;

                b.Container = container;
                b.VideoJobs = new List<TaggedJob>(vjobs.Jobs);
                b.DesiredSize = desiredSize.Value;
                ((VideoJob)vjobs.Jobs[0].Job).BitrateCalculationInfo = b;
            }

            return 
                new SequentialChain(
                    new ParallelChain((Job[])audioStreams),
                    new SequentialChain(vjobs),
                    new SequentialChain(muxJobs));
        }

        private void fixFileNameExtensions(VideoStream video, AudioJob[] audioStreams, ContainerType container)
        {
            AudioEncoderType[] audioCodecs = new AudioEncoderType[audioStreams.Length];
            for (int i = 0; i < audioStreams.Length; i++)
            {
                audioCodecs[i] = audioStreams[i].Settings.EncoderType;
            }
            MuxPath path = mainForm.MuxProvider.GetMuxPath(video.Settings.EncoderType, audioCodecs, container);
            if (path == null)
                return;
            List<AudioType> audioTypes = new List<AudioType>();
            foreach (MuxableType type in path.InitialInputTypes)
            {
                if (type.outputType is VideoType)
                {
                    // see http://forum.doom9.org/showthread.php?p=1243370#post1243370
                    if ((mainForm.Settings.ForceRawAVCExtension) && (video.Settings.EncoderType == VideoEncoderType.X264))
                         video.Output = Path.ChangeExtension(video.Output, ".264");
                    else video.Output = Path.ChangeExtension(video.Output, type.outputType.Extension);
                    video.VideoType = type;
                }
                if (type.outputType is AudioType)
                {
                    audioTypes.Add((AudioType)type.outputType);
                }
            }
            AudioEncoderProvider aProvider = new AudioEncoderProvider();
            for (int i = 0; i < audioStreams.Length; i++)
            {
                AudioType[] types = aProvider.GetSupportedOutput(audioStreams[i].Settings.EncoderType);
                foreach (AudioType type in types)
                {
                    if (audioTypes.Contains(type))
                    {
                        audioStreams[i].Output = Path.ChangeExtension(audioStreams[i].Output,
                            type.Extension);
                        break;
                    }
                }
            }
        }

        public static SubtitleType guessSubtitleType(string p)
        {
            foreach (SubtitleType type in ContainerManager.SubtitleTypes.Values)
            {
                if (Path.GetExtension(p.ToLower()) == "." + type.Extension)
                    return type;
            }
            return null;
        }

        public static VideoType guessVideoType(string p)
        {
            foreach (VideoType type in ContainerManager.VideoTypes.Values)
            {
                if (Path.GetExtension(p.ToLower()) == "." + type.Extension)
                    return type;
            }
            return null;
        }
 
        public static AudioType guessAudioType(string p)
        {
            foreach (AudioType type in ContainerManager.AudioTypes.Values)
            {
                if (Path.GetExtension(p.ToLower()) == "." + type.Extension)
                    return type;
            }
            return null;
        }

        public static ChapterType guessChapterType(string p)
        {
            foreach (ChapterType type in ContainerManager.ChapterTypes.Values)
            {
                if (Path.GetExtension(p.ToLower()) == "." + type.Extension)
                    return type;
            }
            return null;
        }

        public static DeviceType guessDeviceType(string p)
        {
            foreach (DeviceType type in ContainerManager.DeviceTypes.Values)
            {
                if (p == type.Extension)
                    return type;
            }
            return null;
        }

        public static MuxableType guessVideoMuxableType(string p, bool useMediaInfo)
        {
            if (string.IsNullOrEmpty(p))
                return null;
            if (useMediaInfo)
            {
                MediaInfoFile info = new MediaInfoFile(p);
                if (info.Info.HasVideo)
                    return new MuxableType(info.VideoType, info.VCodec);
                // otherwise we may as well try the other route too
            }
            VideoType vType = guessVideoType(p);
            if (vType != null)
            {
                if (vType.SupportedCodecs.Length == 1)
                    return new MuxableType(vType, vType.SupportedCodecs[0]);
                else
                    return new MuxableType(vType, null);
            }
            return null;
        }

        public static MuxableType guessAudioMuxableType(string p, bool useMediaInfo)
        {
            if (string.IsNullOrEmpty(p))
                return null;
            if (useMediaInfo)
            {
                MediaInfoFile info = new MediaInfoFile(p);
                if (info.AudioType != null)
                    return new MuxableType(info.AudioType, info.ACodecs[0]);
            }
            AudioType aType = guessAudioType(p);
            if (aType != null)
            {
                if (aType.SupportedCodecs.Length == 1)
                    return new MuxableType(aType, aType.SupportedCodecs[0]);
                else
                    return new MuxableType(aType, null);
            }
            return null;
        }
        #endregion

        public static string createSimpleAvisynthScript(string filename)
        {
            PossibleSources sourceType = PossibleSources.directShow;
            if (filename.ToLower().EndsWith(".vdr"))
                sourceType = PossibleSources.vdr;
            string outputFile = filename + ".avs";
            if (File.Exists(outputFile))
            {
                DialogResult response = MessageBox.Show("The file, '" + outputFile + "' already exists.\r\n Do you want to overwrite it?",
                    "File already exists", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (response == DialogResult.No)
                    return null;
            }
            try
            {
                StreamWriter output = new StreamWriter(outputFile);
                output.WriteLine(
                    ScriptServer.GetInputLine(filename, false, sourceType, false, false, false, -1, false));
                output.Close();
            }
            catch (IOException)
            {
                return null;
            }
            return outputFile;
        }

        public static string convertChaptersTextFileTox264QPFile(string filename, double framerate)
        {
            StreamWriter sw = null;
            string qpfile = "";
            if (File.Exists(filename))
            {
                StreamReader sr = null;
                string line = null;
                qpfile = Path.ChangeExtension(filename, ".qpf");
                sw = new StreamWriter(qpfile, false, System.Text.Encoding.Default);
                try
                {
                    sr = new StreamReader(filename);
                    Chapter chap = new Chapter();
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.IndexOf("NAME") == -1) // chapter time
                        {
                            string tc = line.Substring(line.IndexOf("=") + 1);
                            chap.timecode = tc;
                            int chapTime = Util.getTimeCode(chap.timecode);
                            int frameNumber = Util.convertTimecodeToFrameNumber(chapTime, framerate);
                            sw.WriteLine(frameNumber.ToString() + " K");
                        }
                    }

                }
                catch (Exception f)
                {
                    MessageBox.Show(f.Message);
                }
                finally
                {
                    if (sw != null)
                    {
                        try
                        {
                            sw.Close();
                        }
                        catch (Exception f)
                        {
                            MessageBox.Show(f.Message);
                        }
                    }
                }                
            }
            return qpfile;
        }

        public static string GenerateCombinedFilter(OutputFileType[] types)
        {
            StringBuilder initialFilterName = new StringBuilder();
            StringBuilder initialFilter = new StringBuilder();
            StringBuilder allSmallFilters = new StringBuilder();
            initialFilterName.Append("All supported files (");
            foreach (OutputFileType type in types)
            {
                initialFilter.Append(type.OutputFilter);
                initialFilter.Append(";");
                initialFilterName.Append(type.OutputFilter);
                initialFilterName.Append(", ");
                allSmallFilters.Append(type.OutputFilterString);
                allSmallFilters.Append("|");
            }

            string initialFilterTrimmed = initialFilterName.ToString().TrimEnd(' ', ',') + ")|" +
                initialFilter.ToString();

            if (types.Length > 1)
                return initialFilterTrimmed + "|" + allSmallFilters.ToString().TrimEnd('|');
            else
                return allSmallFilters.ToString().TrimEnd('|');
        }

        public static void getAvisynthVersion(out string FileVersion, out string FileDate, out bool PropExists)
        {
            FileVersion = string.Empty;
            FileDate = string.Empty;
            PropExists = false;

            string systempath = Environment.GetFolderPath(Environment.SpecialFolder.System);

            if (File.Exists(systempath + "\\avisynth.dll"))
            {
                FileVersionInfo FileProperties = FileVersionInfo.GetVersionInfo(systempath + "\\avisynth.dll");
                FileVersion = FileProperties.FileVersion;
                FileDate = File.GetLastWriteTimeUtc(systempath + "\\avisynth.dll").ToString();
                PropExists = true;
            }
#if x86
            else
            {
                // on x64, try the SysWOW64 folder
                string syswow64path = Environment.GetFolderPath(Environment.SpecialFolder.System).Substring(0, 11) + "\\SysWOW64";
                if (Directory.Exists(syswow64path))
                {
                    if (File.Exists(syswow64path + "\\avisynth.dll"))
                    {
                        FileVersionInfo FileProperties = FileVersionInfo.GetVersionInfo(syswow64path + "\\avisynth.dll");
                        FileVersion = FileProperties.FileVersion;
                        FileDate = File.GetLastWriteTimeUtc(syswow64path + "\\avisynth.dll").ToString();
                        PropExists = true;
                    }
                }
            }
#endif
        }

        public static bool isDGIIndexerAvailable()
        {
            // check if the license file is available
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(MainForm.Instance.Settings.DgnvIndexPath), "license.txt")))
                return false;

            // DGI is not available in a RDP connection
            if (System.Windows.Forms.SystemInformation.TerminalServerSession == true)
                return false;

            // check if the indexer is available
            if (!File.Exists(MainForm.Instance.Settings.DgnvIndexPath))
                return false;

            return true;
        }

        public static string getAssumeFPS(double fps, string strInput)
        {
            if (fps <= 0)
            {
                if (!File.Exists(strInput))
                    return String.Empty;
                if (strInput.ToLower().EndsWith(".ffindex"))
                    strInput = strInput.Substring(0, strInput.Length - 8);
                if (Path.GetExtension(strInput).ToLower().Equals(".avs"))
                {
                    fps = GetFPSFromAVSFile(strInput);
                    if (fps <= 0)
                        return String.Empty;
                }
                else
                {
                    MediaInfoFile oInfo = new MediaInfoFile(strInput);
                    if (oInfo.Info.HasVideo && oInfo.Info.FPS > 0)
                        fps = oInfo.Info.FPS;
                    else
                        return String.Empty;
                }
            }

            string strAssumeFPS = ".AssumeFPS(";
            string strFPS = Math.Round(fps, 3).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            switch (strFPS)
            {
                case "23.976": strAssumeFPS += "24000,1001"; break;
                case "29.970": strAssumeFPS += "30000,1001"; break;
                case "59.940": strAssumeFPS += "60000,1001"; break;
                case "119.880": strAssumeFPS += "120000,1001"; break;
                case "24.000": strAssumeFPS += "24,1"; break;
                case "25.000": strAssumeFPS += "25,1"; break;
                case "50.000": strAssumeFPS += "50,1"; break;
                case "100.000": strAssumeFPS += "100,1"; break;
                default: strAssumeFPS += strFPS; break;
            }
            return strAssumeFPS + ")";
        }

        public static double GetFPSFromAVSFile(String strAVSScript)
        {
            try
            {
                if (!Path.GetExtension(strAVSScript).ToLower().Equals(".avs"))
                    return 0;
                using (AviSynthScriptEnvironment env = new AviSynthScriptEnvironment())
                {
                    using (AviSynthClip a = env.OpenScriptFile(strAVSScript))
                        if (a.HasVideo)
                            return (double)a.raten / (double)a.rated;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }
	#region helper structs
	/// <summary>
	/// helper structure for cropping
	/// holds the crop values for all 4 edges of a frame
	/// </summary>
	[LogByMembers]
    public sealed class CropValues
	{
		public int left, top, right, bottom;
        public CropValues Clone()
        {
            return (CropValues)this.MemberwiseClone();
        }
	}

    public class SubtitleInfo
    {
        private string name;
        private int index;
        public SubtitleInfo(string name, int index)
        {
            this.name = name;
            this.index = index;
        }
        public string Name
        {
            get { return name; }
            set { name = value; }
        }
        public int Index
        {
            get { return index; }
            set { index = value; }
        }
        public override string ToString()
        {
            string fullString = "[" + this.index.ToString("D2") + "] - " + this.name;
            return fullString.Trim();
        }
    }
	#endregion
}
