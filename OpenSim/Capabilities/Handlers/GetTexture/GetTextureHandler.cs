/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.IO;
using System.Web;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenMetaverse.Imaging;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Services.Interfaces;
using Caps = OpenSim.Framework.Capabilities.Caps;

namespace OpenSim.Capabilities.Handlers
{
    public class GetTextureHandler
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IAssetService m_assetService;

        public const string DefaultFormat = "x-j2c";

        public GetTextureHandler(IAssetService assService)
        {
            m_assetService = assService;
        }

        public Hashtable Handle(Hashtable request)
        {
            Hashtable ret = new Hashtable();
            ret["int_response_code"] = (int)System.Net.HttpStatusCode.NotFound;
            ret["content_type"] = "text/plain";
            ret["int_bytes"] = 0;
            string textureStr = (string)request["texture_id"];
            string format = (string)request["format"];

            //m_log.DebugFormat("[GETTEXTURE]: called {0}", textureStr);

            if (m_assetService == null)
            {
                m_log.Error("[GETTEXTURE]: Cannot fetch texture " + textureStr + " without an asset service");
            }

            UUID textureID;
            if (!String.IsNullOrEmpty(textureStr) && UUID.TryParse(textureStr, out textureID))
            {
//                m_log.DebugFormat("[GETTEXTURE]: Received request for texture id {0}", textureID);

                string[] formats;
                if (!string.IsNullOrEmpty(format))
                {
                    formats = new string[1] { format.ToLower() };
                }
                else
                {
                    formats = new string[1] { DefaultFormat }; // default
                    if (((Hashtable)request["headers"])["Accept"] != null)
                        formats = WebUtil.GetPreferredImageTypes((string)((Hashtable)request["headers"])["Accept"]);
                    if (formats.Length == 0)
                        formats = new string[1] { DefaultFormat }; // default

                }
                // OK, we have an array with preferred formats, possibly with only one entry
                bool foundtexture = false;
                foreach (string f in formats)
                {
                    foundtexture = FetchTexture(request, ret, textureID, f);
                    if (foundtexture)
                        break;
                }
                if (!foundtexture)
                {
                    ret["int_response_code"] = 404;
                    ret["error_status_text"] = "not found";
                    ret["str_response_string"] = "not found";
                    ret["content_type"] = "text/plain";
                    ret["int_bytes"] = 0;
                }
            }
            else
            {
                m_log.Warn("[GETTEXTURE]: Failed to parse a texture_id from GetTexture request: " + (string)request["uri"]);
            }

//            m_log.DebugFormat(
//                "[GETTEXTURE]: For texture {0} sending back response {1}, data length {2}",
//                textureID, httpResponse.StatusCode, httpResponse.ContentLength);
            return ret;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="httpRequest"></param>
        /// <param name="httpResponse"></param>
        /// <param name="textureID"></param>
        /// <param name="format"></param>
        /// <returns>False for "caller try another codec"; true otherwise</returns>
        private bool FetchTexture(Hashtable request, Hashtable response, UUID textureID, string format)
        {
//            m_log.DebugFormat("[GETTEXTURE]: {0} with requested format {1}", textureID, format);
            AssetBase texture;

            string fullID = textureID.ToString();
            if (format != DefaultFormat)
                fullID = fullID + "-" + format;

            // try the cache
            texture = m_assetService.GetCached(fullID);

            if (texture == null)
            {
                //m_log.DebugFormat("[GETTEXTURE]: texture was not in the cache");

                // Fetch locally or remotely. Misses return a 404
                texture = m_assetService.Get(textureID.ToString());

                if (texture != null)
                {
                    if (texture.Type != (sbyte)AssetType.Texture)
                        return true;

                    if (format == DefaultFormat)
                    {
                        WriteTextureData(request, response, texture, format);
                        return true;
                    }
                    else
                    {
                        AssetBase newTexture = new AssetBase(texture.ID + "-" + format, texture.Name, (sbyte)AssetType.Texture, texture.Metadata.CreatorID);
                        newTexture.Data = ConvertTextureData(texture, format);
                        if (newTexture.Data.Length == 0)
                            return false; // !!! Caller try another codec, please!

                        newTexture.Flags = AssetFlags.Collectable;
                        newTexture.Temporary = true;
                        newTexture.Local = true;
                        m_assetService.Store(newTexture);
                        WriteTextureData(request, response, newTexture, format);
                        return true;
                    }
                }
           }
           else // it was on the cache
           {
               //m_log.DebugFormat("[GETTEXTURE]: texture was in the cache");
               WriteTextureData(request, response, texture, format);
               return true;
           }

            //response = new Hashtable();


            //WriteTextureData(request,response,null,format);
            // not found
            //m_log.Warn("[GETTEXTURE]: Texture " + textureID + " not found");
            return false;
        }

        private void WriteTextureData(Hashtable request, Hashtable response, AssetBase texture, string format)
        {
            Hashtable headers = new Hashtable();
            response["headers"] = headers;

            string range = String.Empty;

            if (((Hashtable)request["headers"])["range"] != null)
                range = (string)((Hashtable)request["headers"])["range"];

            else if (((Hashtable)request["headers"])["Range"] != null)
                range = (string)((Hashtable)request["headers"])["Range"];

            if (!String.IsNullOrEmpty(range)) // JP2's only
            {
                // Range request
                int start, end;
                if (Util.TryParseHttpRange(range, out start, out end))
                {
                    // Before clamping start make sure we can satisfy it in order to avoid
                    // sending back the last byte instead of an error status
                    if (start >= texture.Data.Length)
                    {
//                        m_log.DebugFormat(
//                            "[GETTEXTURE]: Client requested range for texture {0} starting at {1} but texture has end of {2}",
//                            texture.ID, start, texture.Data.Length);

                        // Stricly speaking, as per http://www.w3.org/Protocols/rfc2616/rfc2616-sec14.html, we should be sending back
                        // Requested Range Not Satisfiable (416) here.  However, it appears that at least recent implementations
                        // of the Linden Lab viewer (3.2.1 and 3.3.4 and probably earlier), a viewer that has previously
                        // received a very small texture  may attempt to fetch bytes from the server past the
                        // range of data that it received originally.  Whether this happens appears to depend on whether
                        // the viewer's estimation of how large a request it needs to make for certain discard levels
                        // (http://wiki.secondlife.com/wiki/Image_System#Discard_Level_and_Mip_Mapping), chiefly discard
                        // level 2.  If this estimate is greater than the total texture size, returning a RequestedRangeNotSatisfiable
                        // here will cause the viewer to treat the texture as bad and never display the full resolution
                        // However, if we return PartialContent (or OK) instead, the viewer will display that resolution.

//                        response.StatusCode = (int)System.Net.HttpStatusCode.RequestedRangeNotSatisfiable;
                        // viewers don't seem to handle RequestedRangeNotSatisfiable and keep retrying with same parameters
                        response["int_response_code"] = (int)System.Net.HttpStatusCode.NotFound;
                    }
                    else
                    {
                        // Handle the case where no second range value was given.  This is equivalent to requesting
                        // the rest of the entity.
                        if (end == -1)
                            end = int.MaxValue;

                        end = Utils.Clamp(end, 0, texture.Data.Length - 1);
                        start = Utils.Clamp(start, 0, end);
                        int len = end - start + 1;

//                        m_log.Debug("Serving " + start + " to " + end + " of " + texture.Data.Length + " bytes for texture " + texture.ID);

                        response["content-type"] = texture.Metadata.ContentType;
                        response["int_response_code"] = (int)System.Net.HttpStatusCode.PartialContent;
                        headers["Content-Range"] = String.Format("bytes {0}-{1}/{2}", start, end, texture.Data.Length);

                        byte[] d = new byte[len];
                        Array.Copy(texture.Data, start, d, 0, len);
                        response["bin_response_data"] = d;
                        response["int_bytes"] = len;
                    }
                }
                else
                {
                    m_log.Warn("[GETTEXTURE]: Malformed Range header: " + range);
                    response["int_response_code"] = (int)System.Net.HttpStatusCode.BadRequest;
                }
            }
            else // JP2's or other formats
            {
                // Full content request
                response["int_response_code"] = (int)System.Net.HttpStatusCode.OK;
                if (format == DefaultFormat)
                    response["content_type"] = texture.Metadata.ContentType;
                else
                    response["content_type"] = "image/" + format;

                response["bin_response_data"] = texture.Data;
                response["int_bytes"] = texture.Data.Length;

//                response.Body.Write(texture.Data, 0, texture.Data.Length);
            }

//            if (response.StatusCode < 200 || response.StatusCode > 299)
//                m_log.WarnFormat(
//                    "[GETTEXTURE]: For texture {0} requested range {1} responded {2} with content length {3} (actual {4})",
//                    texture.FullID, range, response.StatusCode, response.ContentLength, texture.Data.Length);
//            else
//                m_log.DebugFormat(
//                    "[GETTEXTURE]: For texture {0} requested range {1} responded {2} with content length {3} (actual {4})",
//                    texture.FullID, range, response.StatusCode, response.ContentLength, texture.Data.Length);
        }

        private byte[] ConvertTextureData(AssetBase texture, string format)
        {
            m_log.DebugFormat("[GETTEXTURE]: Converting texture {0} to {1}", texture.ID, format);
            byte[] data = new byte[0];

            MemoryStream imgstream = new MemoryStream();
            Bitmap mTexture = null;
            ManagedImage managedImage = null;
            Image image = null;

            try
            {
                // Taking our jpeg2000 data, decoding it, then saving it to a byte array with regular data

                // Decode image to System.Drawing.Image
                if (OpenJPEG.DecodeToImage(texture.Data, out managedImage, out image) && image != null)
                {
                    // Save to bitmap
                    mTexture = new Bitmap(image);

                    using(EncoderParameters myEncoderParameters = new EncoderParameters())
                    {
                        myEncoderParameters.Param[0] = new EncoderParameter(Encoder.Quality,95L);

                        // Save bitmap to stream
                        ImageCodecInfo codec = GetEncoderInfo("image/" + format);
                        if (codec != null)
                        {
                            mTexture.Save(imgstream, codec, myEncoderParameters);
                        // Write the stream to a byte array for output
                            data = imgstream.ToArray();
                        }
                        else
                            m_log.WarnFormat("[GETTEXTURE]: No such codec {0}", format);
                    }
                }
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[GETTEXTURE]: Unable to convert texture {0} to {1}: {2}", texture.ID, format, e.Message);
            }
            finally
            {
                // Reclaim memory, these are unmanaged resources
                // If we encountered an exception, one or more of these will be null
                if (mTexture != null)
                    mTexture.Dispose();

                if (image != null)
                    image.Dispose();

                if(managedImage != null)
                    managedImage.Clear();
                if (imgstream != null)
                    imgstream.Dispose();
            }

            return data;
        }

        // From msdn
        private static ImageCodecInfo GetEncoderInfo(String mimeType)
        {
            ImageCodecInfo[] encoders;
            encoders = ImageCodecInfo.GetImageEncoders();
            for (int j = 0; j < encoders.Length; ++j)
            {
                if (encoders[j].MimeType == mimeType)
                    return encoders[j];
            }
            return null;
        }
    }
}
