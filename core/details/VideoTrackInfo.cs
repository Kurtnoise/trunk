﻿// ****************************************************************************
// 
// Copyright (C) 2005-2012 Doom9 & al
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

using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;

namespace MeGUI
{
    public class VideoTrackInfo : TrackInfo
    {
        public VideoTrackInfo() : this(0, 0, null, null, null)
        {
        }

        public VideoTrackInfo(int trackID, int mmgTrackID, string language, string name, string codec)
        {
            base.TrackType = TrackType.Video;
            base.Language = language;
            base.Name = name;
            base.TrackID = trackID;
            base.MMGTrackID = mmgTrackID;
            base.Codec = codec;
        }

        public override string ToString()
        {
            string fullString = "[" + TrackID + "] - " + this.Codec;
            if (!string.IsNullOrEmpty(Language))
                fullString += " / " + Language;
            return fullString.Trim();
        }
    }
}