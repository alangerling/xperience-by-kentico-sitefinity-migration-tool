using Kentico.Xperience.UMT.Model;

namespace Migration.Toolkit.Sitefinity.Model;
public interface IMediaDependencies
{
    public IDictionary<Guid, ContentItemSimplifiedModel> MediaFiles { get; set; }
}
