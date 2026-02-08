using System.Collections.Generic;

namespace CMSAPI.Application.Models;

public class PagedResult<T>
{
    public IEnumerable<T> Data { get; set; } = Enumerable.Empty<T>();
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public long TotalItems { get; set; }
    public int ItemsPerPage { get; set; }
}
