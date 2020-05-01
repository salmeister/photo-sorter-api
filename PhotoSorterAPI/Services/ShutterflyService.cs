using PhotoSorterAPI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace PhotoSorterAPI.Services
{
    public class ShutterflyService
    {
        private static readonly string shutterflyHost = "https://ws.shutterfly.com";
        private static readonly string shutterflyUpHost = "http://up3.shutterfly.com";
        private static readonly Encoding encoding = Encoding.UTF8;

        public static string GetAuthenticationID(string email, string psswd, string oflyAppID, string sharedSecret)
        {
            string oflyHashMeth = "SHA1";
            string oflyTimestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");

            string postAuthURL = shutterflyHost + "/user/" + email + "/auth?oflyAppId=" + oflyAppID;

            //Timestamp formatting
            int index = oflyTimestamp.LastIndexOf(':');
            oflyTimestamp = oflyTimestamp.Remove(index, 1).Insert(index, "");

            //Get API Signature
            string rawSignature = sharedSecret + "/user/" + email + "/auth?oflyAppId=" + oflyAppID + "&oflyHashMeth=" + oflyHashMeth + "&oflyTimestamp=" + oflyTimestamp;
            byte[] encodedSignature = encoding.GetBytes(rawSignature);
            byte[] encryptedSignature;

            using (SHA1 sha = new SHA1CryptoServiceProvider())
            {
                // This is one implementation of the abstract class SHA1.
                encryptedSignature = sha.ComputeHash(encodedSignature);
            }

            string apiSignature = BitConverter.ToString(encryptedSignature);
            apiSignature = apiSignature.Replace("-", "");

            //Get existing authorization ID
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                { "oflyHashMeth", oflyHashMeth },
                { "oflyApiSig", apiSignature },
                { "oflyTimestamp", oflyTimestamp }
            };

            string authenticationID;
            int counter = 0;
            do
            {
                authenticationID = GetNewAuthID(postAuthURL, psswd, headers);
                counter++;
            }
            while (counter < 10 && authenticationID == "");

            string result = "Failed: no authentication ID.";
            if (!String.IsNullOrWhiteSpace(authenticationID))
            {
                result = authenticationID;
            }

            return result;
        }

        private static string GetNewAuthID(string url, string psswd, Dictionary<string, string> headers)
        {
            //POST message
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<entry xmlns=\"http://www.w3.org/2005/Atom\" xmlns:user=\"http://user.openfly.shutterfly.com/v1.0\">");
            sb.Append("<category term=\"user\" scheme=\"http://openfly.shutterfly.com/v1.0\" />");
            sb.Append("<user:password>" + psswd + "</user:password></entry>");
            string msg = sb.ToString();

            
            string result = PostMethod(url, msg, "text/xml", headers);
            if (result != "")
            {
                XmlDocument atomXML = new XmlDocument();
                using (TextReader tr = new StringReader(result))
                {
                    atomXML.Load(tr);
                }
                if (atomXML.GetElementsByTagName("user:newAuthToken").Count > 0)
                {
                    result = atomXML.DocumentElement["user:newAuthToken"].InnerText;
                }
            }
            return result;
        }

        public static string Upload(string authenticationID, string oflyAppID, string month, string year, string filePath, string fileName)
        {
            string uploadURL = shutterflyUpHost + "/images?oflyAppId=" + oflyAppID;

            return SendToShutterfly(uploadURL, authenticationID, month, year, Path.Combine(filePath, fileName), fileName);
        }

        private static string SendToShutterfly(string uploadURL, string authenticationID, string month, string year, string filePath, string fileName)
        {
            // Read file data
            FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            byte[] data = new byte[fs.Length];
            fs.Read(data, 0, data.Length);
            fs.Close();

            // Generate post objects
            Dictionary<string, object> postParameters = new Dictionary<string, object>
            {
                { "AuthenticationID", authenticationID },
                { "Image.AlbumName", month },
                { "Image.FolderName", year },
                { "Image.Data", new FileParameter(data, fileName, "image/jpeg") }
            };

            // Create request and receive response
            HttpWebResponse webResponse = MultipartFormDataPost(uploadURL, postParameters);

            string result;
            try
            {
                Stream stream2 = webResponse.GetResponseStream();
                using (StreamReader reader2 = new StreamReader(stream2))
                {
                    result = reader2.ReadToEnd();
                }

                if (result != "")
                {
                    XmlDocument atomXML = new XmlDocument();
                    using (TextReader tr = new StringReader(result))
                    {
                        atomXML.Load(tr);
                    }
                    if (atomXML.GetElementsByTagName("upload:errMessage").Count > 0)
                    {
                        result = atomXML.DocumentElement["upload:errMessage"].InnerText;
                    }
                }
                if (result.ToUpper() != "OK")
                {
                    result = "Failed: " + result;
                }
            }
            catch (Exception ex)
            {
                result = "Failed: " + ex.Message;
            }

            return result;
        }

        public static HttpWebResponse MultipartFormDataPost(string postUrl, Dictionary<string, object> postParameters)
        {
            string formDataBoundary = String.Format("----------{0:N}", Guid.NewGuid());
            string contentType = "multipart/form-data; boundary=" + formDataBoundary;

            byte[] formData = GetMultipartFormData(postParameters, formDataBoundary);

            return PostForm(postUrl, contentType, formData);
        }

        private static HttpWebResponse PostForm(string postUrl, string contentType, byte[] formData)
        {
            if (!(WebRequest.Create(postUrl) is HttpWebRequest request))
            {
                throw new NullReferenceException("request is not a http request");
            }

            // Set up the request properties.
            request.Method = "POST";
            request.ContentType = contentType;
            request.ContentLength = formData.Length;

            // You could add authentication here as well if needed:
            // request.PreAuthenticate = true;
            // request.AuthenticationLevel = System.Net.Security.AuthenticationLevel.MutualAuthRequested;
            // request.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(System.Text.Encoding.Default.GetBytes("username" + ":" + "password")));

            // Send the form data to the request.
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(formData, 0, formData.Length);
                requestStream.Close();
            }

            return request.GetResponse() as HttpWebResponse;
        }

        private static byte[] GetMultipartFormData(Dictionary<string, object> postParameters, string boundary)
        {
            Stream formDataStream = new System.IO.MemoryStream();
            bool needsCLRF = false;

            foreach (var param in postParameters)
            {
                if (needsCLRF)
                    formDataStream.Write(encoding.GetBytes("\r\n"), 0, encoding.GetByteCount("\r\n"));

                needsCLRF = true;

                if (param.Value is FileParameter fileToUpload)
                {
                    // Add just the first part of this param, since we will write the file data directly to the Stream
                    string header = string.Format("--{0}\r\nContent-Disposition: form-data; name=\"{1}\"; filename=\"{2}\";\r\nContent-Type: {3}\r\n\r\n",
                        boundary,
                        param.Key,
                        fileToUpload.FileName ?? param.Key,
                        fileToUpload.ContentType ?? "application/octet-stream");

                    formDataStream.Write(encoding.GetBytes(header), 0, encoding.GetByteCount(header));

                    // Write the file data directly to the Stream, rather than serializing it to a string.
                    formDataStream.Write(fileToUpload.File, 0, fileToUpload.File.Length);
                }
                else
                {
                    string postData = string.Format("--{0}\r\nContent-Disposition: form-data; name=\"{1}\"\r\n\r\n{2}",
                        boundary,
                        param.Key,
                        param.Value);
                    formDataStream.Write(encoding.GetBytes(postData), 0, encoding.GetByteCount(postData));
                }
            }

            // Add the end of the request.  Start with a newline
            string footer = "\r\n--" + boundary + "--\r\n";
            formDataStream.Write(encoding.GetBytes(footer), 0, encoding.GetByteCount(footer));

            // Dump the Stream into a byte[]
            formDataStream.Position = 0;
            byte[] formData = new byte[formDataStream.Length];
            formDataStream.Read(formData, 0, formData.Length);
            formDataStream.Close();

            return formData;
        }

        private static string GetMethod(string url, Dictionary<string, string> headers)
        {
            string result = string.Empty;
            var myHttpWebRequest = (HttpWebRequest)WebRequest.Create(url);

            myHttpWebRequest.Method = "GET";
            //myHttpWebRequest.Timeout = Convert.ToInt32(999999999);

            //Set Headers
            foreach (var header in headers)
            {
                myHttpWebRequest.Headers[header.Key] = header.Value;
            }

            try
            {
                var myHttpWebResponse = (HttpWebResponse)myHttpWebRequest.GetResponse();
                using var reader = new StreamReader(myHttpWebResponse.GetResponseStream(), Encoding.UTF8);
                result = reader.ReadToEnd();
                reader.Close();
                reader.Dispose();
                myHttpWebResponse.Close();
            }
            catch
            {
            }
            return result;
        }

        private static string PostMethod(string url, string msg, string contentType, Dictionary<string, string> headers)
        {
            string result = string.Empty;
            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            var myHttpWebRequest = (HttpWebRequest)WebRequest.Create(url);

            myHttpWebRequest.Method = "POST";
            myHttpWebRequest.ContentType = contentType;
            myHttpWebRequest.ContentLength = buffer.Length;
            //myHttpWebRequest.Timeout = Convert.ToInt32(999999999);

            //Set Headers
            foreach (var header in headers)
            {
                myHttpWebRequest.Headers[header.Key] = header.Value;
            }

            //Send Request
            try
            {
                using (var request = myHttpWebRequest.GetRequestStream())
                {
                    request.Write(buffer, 0, buffer.Length);
                    request.Close();
                    request.Dispose();
                }

                //Get Response
                var myHttpWebResponse = (HttpWebResponse)myHttpWebRequest.GetResponse();
                using var reader = new StreamReader(myHttpWebResponse.GetResponseStream(), Encoding.UTF8);
                result = reader.ReadToEnd();
                reader.Close();
                reader.Dispose();
                myHttpWebResponse.Close();
            }
            catch (Exception ex)
            {
            }

            return result;
        }
    }
}
