using System.Collections.Generic;

namespace EFCore.MyCustom.Tests.Models;

public class Blog
{
    public int BlogId { get; set; }
    public string Url { get; set; }

    public List<Post> Posts { get; } = new();
}
