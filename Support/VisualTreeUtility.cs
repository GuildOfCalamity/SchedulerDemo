using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SchedulerDemo;

/// <summary>
/// I re-wrote this based on an old post from the MSDN WPF forum.
/// </summary>
public class VisualTreeUtility
{
    /// <summary>
    /// Find the child element in the visual tree of reference visual.
    /// </summary>
    /// <typeparam name="T">The type of element you want to examine.</typeparam>
    /// <param name="reference">The visual whose visual tree is traversed.</param>
    /// <returns>Return the element being first found in the visual tree</returns>
    public static T? FindChildVisual<T>(Visual reference) where T : Visual
    {
        T? targetVisual = null;
        VisualTreeWalker walker = new VisualTreeWalker();
        walker.VisualVisited += delegate (object sender, VisualVisitedEventArgs e)
        {
            if (e.VisitedVisual is T)
                targetVisual = e.VisitedVisual as T;
        };

        walker.Walk(reference);
        return targetVisual;
    }

    /// <summary>
    /// Find the child elements in the visual tree of reference visual.
    /// </summary>
    /// <typeparam name="T">The type of element you want to examine.</typeparam>
    /// <param name="reference">The visual whose visual tree is traversed.</param>
    /// <returns>Return the elements being found in the visual tree</returns>
    public static T[] FindChildVisuals<T>(Visual reference) where T : Visual
    {
        List<T> visuals = new List<T>();
        VisualTreeWalker walker = new VisualTreeWalker();
        walker.VisualVisited += delegate (object sender, VisualVisitedEventArgs e)
        {
            if (e.VisitedVisual is T)
                visuals.Add((T)e.VisitedVisual);
        };

        walker.Walk(reference);
        return visuals.ToArray();
    }
}

#region [Supporting Classes]
public delegate void VisualVisitedEventHandler(Object sender, VisualVisitedEventArgs e);

/// <summary>
/// This class represents a wrapper around the arguments passed to the VisualVisited event handler.
/// </summary>
/// <remarks>
/// This class is mutated from John Grossman's VisualTreeWalker implementation.
/// </remarks>
public class VisualVisitedEventArgs : EventArgs
{
    private Visual visitedVisual;
    private Int32 currentDepth;

    public VisualVisitedEventArgs(Visual visitedVisual, Int32 currentDepth)
    {
        this.visitedVisual = visitedVisual;
        this.currentDepth = currentDepth;
    }

    /// <summary>
    /// Get the visual currently visited by the VisualTreeWalker.
    /// </summary>
    public Visual VisitedVisual => visitedVisual;

    /// <summary>
    /// Get the depth of visual tree that the VisualTreeWalker is currently in.
    /// </summary>
    public Int32 CurrentDepth => currentDepth;
}

/// <summary>
/// Represents a class which can walk through the visual tree.
/// </summary>
public class VisualTreeWalker
{
    private Int32 visualCount;

    /// <summary>
    /// Presents the VisualVisited event.
    /// </summary>
    public event VisualVisitedEventHandler? VisualVisited;

    /// <summary>
    /// Begin to walk through the visual tree starting from the reference visual.
    /// </summary>
    /// <param name="reference">The visual whose visual tree will be walked through.</param>
    /// <returns>The number of visuals visited by the VisualTreeWalker.</returns>
    public Int32 Walk(Visual reference)
    {
        this.visualCount = 0;
        this.TraverseVisuals(reference, 1);
        return this.visualCount;
    }

    private void TraverseVisuals(Visual visual, Int32 currentDepth)
    {
        this.visualCount++;
        this.OnVisualVisited(new VisualVisitedEventArgs(visual, currentDepth));

        // GetChildrenCount() can throw a cross-thread exception if not on the UI.
        if (System.Threading.Thread.CurrentThread == Application.Current.Dispatcher.Thread)
        {
            for (Int32 i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            {
                Visual? child = VisualTreeHelper.GetChild(visual, i) as Visual;
                if (child != null)
                    this.TraverseVisuals(child, currentDepth++);
            }
        }
    }

    /// <summary>
    /// Raise the VisualVisited event.
    /// </summary>
    /// <param name="e">Arguments passed to the VisualVisited event handler.</param>
    protected void OnVisualVisited(VisualVisitedEventArgs e)
    {
        if (this.VisualVisited != null)
            this.VisualVisited(this, e);
    }
}
#endregion
