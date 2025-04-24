using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Cmf.CLI.Core;
using Cmf.CLI.Core.Interfaces;
using Cmf.CLI.Core.Objects;
using Cmf.CLI.Core.Repository.Credentials;
using Cmf.CLI.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Utils;
using SMBLibrary;
using SMBLibrary.Client;

namespace Core.Objects
{
    public class CIFSClient : ICIFSClient
    {

        public string Server { get; private set; }
        public List<SharedFolder> SharedFolders { get; private set; }
        public bool IsConnected => _isConnected.Value;

        private ISMBClient _smbClient;
        private ICredential _credentials;
        protected Lazy<bool> _isConnected;

        [Obsolete("Only one share per CIFSClient is supported now. Remove this and refactor the rest of the code.")]
        public CIFSClient(string server, IEnumerable<Uri> uris, ISMBClient smbClient = null) : this(uris.Single())
        { }

        public CIFSClient(Uri uri, ISMBClient smbClient = null)
        {
            var authStore = ExecutionContext.ServiceProvider.GetService<IRepositoryAuthStore>();

            Server = uri.Host;
            
            _smbClient = smbClient ?? new SMB2Client();
            _credentials = authStore.GetCredentialsFor<CIFSRepositoryCredentials>(authStore.GetOrLoad().GetAwaiter().GetResult(), uri.AbsoluteUri);
            _isConnected = new Lazy<bool>(ConnectInternal);

            SharedFolders = [];
            SharedFolders.Add(new SharedFolder(uri, _smbClient));
        }

        protected bool ConnectInternal()
        {
            if (_credentials == null)
            {
                Log.Warning($"CIFS credentials not found for shares: {string.Join(", ", SharedFolders.Select(s => s.Uri))}.");
                return false;
            }

            if (_credentials is not BasicCredential basicCredential)
            {
                throw new InvalidAuthTypeException(_credentials);
            }

            Log.Debug($"Connecting to SMB server {Server} with username {basicCredential.Username}");
            var isConnected = _smbClient.Connect(Server, SMBTransportType.DirectTCPTransport);
            if (!isConnected)
            {
                Log.Debug($"Failed to connect to {Server}");
                Log.Warning($"Failed to connect to {Server}");
                return false;
            }

            var status = _smbClient.Login(basicCredential.Domain, basicCredential.Username, basicCredential.Password);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                Log.Debug($"Fail status {status}");
                Log.Warning($"Failed to login to {Server} with username {basicCredential.Username}");
                return false;
            }

            foreach (var share in SharedFolders)
            {
                share.Load();
            }

            return true;
        }

        public void Connect()
        {
            // Since this is a Lazy<> instance, it will either trigger the connect method, if not yet connected, or
            // do nothing if already connected
            var _ = _isConnected.Value;
        }

        public void Disconnect()
        {
            _smbClient.Disconnect();
            _isConnected = new Lazy<bool>(ConnectInternal);
        }
    }

    public class SharedFolder : ISharedFolder
    {
        public bool Exists { get; private set; }
        private ISMBFileStore _smbFileStore { get; set; }
        private ISMBClient _client { get; set; }
        private string _server { get; set; }
        private string _share { get; set; }
        private string _path { get; set; }
        private Uri _uri { get; set; }

        public Uri Uri => _uri;

        public SharedFolder(Uri uri, ISMBClient client)
        {
            _client = client;
            _server = uri.Host;
            _share = uri.PathAndQuery.Split("/")[1];
            _path = uri.PathAndQuery.Substring(_share.Length + 1).TrimStart('/');
            _uri = uri;
        }

        public void Load()
        {
            _smbFileStore = _client.TreeConnect(_share, out NTStatus status);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                Log.Debug($"Fail status {status}");
                Log.Warning($"Failed to connect to share {_share} on {_server}");
                Exists = false;
            }
            else
            {
                Exists = true;
            }
        }

        public Tuple<Uri, Stream> GetFile(string fileName)
        {
            Tuple<Uri, Stream> fileStream = null;
            var filepath = $"{_path}/{fileName}";
            var status = _smbFileStore.CreateFile(out object fileHandle, out FileStatus fileStatus, filepath, AccessMask.GENERIC_READ, SMBLibrary.FileAttributes.Normal, ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN, CreateOptions.FILE_NON_DIRECTORY_FILE, null);
            if (status == NTStatus.STATUS_SUCCESS)
            {
                var stream = new MemoryStream();
                long bytesRead = 0;
                while (true)
                {
                    status = _smbFileStore.ReadFile(out byte[] data, fileHandle, bytesRead, (int)_client.MaxReadSize);
                    if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_END_OF_FILE)
                    {
                        throw new Exception($"Failed to read file {filepath}");
                    }

                    if (status == NTStatus.STATUS_END_OF_FILE || data.Length == 0)
                    {
                        break;
                    }
                    bytesRead += data.Length;
                    stream.Write(data, 0, data.Length);
                }
                var uri = JoinUris(_uri, fileName);
                fileStream = new Tuple<Uri, Stream>(uri, stream);
            }

            return fileStream;
        }

        protected Uri JoinUris(Uri baseUri, string pathUri)
        {
            if (!baseUri.AbsolutePath.EndsWith('/'))
            {
                var builder = new UriBuilder(baseUri);
                builder.Path += '/';
                baseUri = builder.Uri;
            }

            return new Uri(baseUri, pathUri.TrimStart('/'));
        }
    }
}