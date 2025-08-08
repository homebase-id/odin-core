namespace JsonLd.Normalization
{
    public class ExpandOptions
    {
        private IContextResolver contextResolver;
        public IContextResolver ContextResolver
        {
            get 
            {
                if (contextResolver is null)
                    contextResolver = new ContextResolver();
                return contextResolver;
            }
            set { contextResolver = value; }
        }

        public string Base { get; set; } = null;
        public string ProtectedMode { get; set; } = null;
        public bool IsFrame { get; set; } = false;
        public bool KeepFreeFloatingNodes { get; set; } = false;
    }
}