namespace Sala.Extensions.WinUI;

using MdcAi.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LinqMini;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

public static class VisualTreeTraversal
{

    public static IEnumerable<DependencyObject> Flatten(this IEnumerable<VisualTreeItem> source) =>
        source.RecursiveSelect(i => i.Children)
              .Select(i => i.Item);

    public static IEnumerable<DependencyObject> GetAncestors(this DependencyObject element)
    {
        if (element == null)
            throw new ArgumentNullException(nameof(element));

        var obj = element;

        while (obj != null)
        {
            // WinUI 3 decided LogicalTreeHelper was not trendy enough
            var parent = VisualTreeHelper.GetParent(obj) as DependencyObject;

            if (parent == null)
                yield break;

            yield return parent;
            obj = parent;
        }
    }

}

public class VisualTreeItem
{
    public VisualTreeItem(DependencyObject item, IEnumerable<VisualTreeItem> children)
    {
        Item = item;
        Children = children;
    }

    public DependencyObject Item { get; }
    public IEnumerable<VisualTreeItem> Children { get; }
}