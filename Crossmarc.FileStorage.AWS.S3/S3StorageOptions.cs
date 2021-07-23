namespace Crossmarc.FileStorage.AWS.S3
{
    public class S3StorageOptions
    {
        private string _prefix;

        public string BucketName { get; set; }

        public string Prefix
        {
            get
            {
                return _prefix;
            }
            set
            {
                _prefix = value;

                if (_prefix != null && !_prefix.EndsWith(S3FileStore.DirectoryDelimiterString))
                {
                    _prefix += S3FileStore.DirectoryDelimiterString;
                }
            }
        }
    }
}
