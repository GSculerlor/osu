// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Humanizer;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.Osu.Edit.Blueprints.Sliders.Components
{
    public class PathControlPointVisualiser : CompositeDrawable, IKeyBindingHandler<PlatformAction>, IHasContextMenu
    {
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true; // allow context menu to appear outside of the playfield.

        internal readonly Container<PathControlPointPiece> Pieces;
        internal readonly Container<PathControlPointConnectionPiece> Connections;

        private readonly IBindableList<PathControlPoint> controlPoints = new BindableList<PathControlPoint>();
        private readonly Slider slider;
        private readonly bool allowSelection;

        private InputManager inputManager;

        public Action<List<PathControlPoint>> RemoveControlPointsRequested;

        public PathControlPointVisualiser(Slider slider, bool allowSelection)
        {
            this.slider = slider;
            this.allowSelection = allowSelection;

            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                Connections = new Container<PathControlPointConnectionPiece> { RelativeSizeAxes = Axes.Both },
                Pieces = new Container<PathControlPointPiece> { RelativeSizeAxes = Axes.Both }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            inputManager = GetContainingInputManager();

            controlPoints.CollectionChanged += onControlPointsChanged;
            controlPoints.BindTo(slider.Path.ControlPoints);
        }

        private void onControlPointsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    // If inserting in the path (not appending),
                    // update indices of existing connections after insert location
                    if (e.NewStartingIndex < Pieces.Count)
                    {
                        foreach (var connection in Connections)
                        {
                            if (connection.ControlPointIndex >= e.NewStartingIndex)
                                connection.ControlPointIndex += e.NewItems.Count;
                        }
                    }

                    for (int i = 0; i < e.NewItems.Count; i++)
                    {
                        var point = (PathControlPoint)e.NewItems[i];

                        Pieces.Add(new PathControlPointPiece(slider, point).With(d =>
                        {
                            if (allowSelection)
                                d.RequestSelection = selectPiece;
                        }));

                        Connections.Add(new PathControlPointConnectionPiece(slider, e.NewStartingIndex + i));

                        point.Changed += updatePathTypes;
                    }

                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (var point in e.OldItems.Cast<PathControlPoint>())
                    {
                        Pieces.RemoveAll(p => p.ControlPoint == point);
                        Connections.RemoveAll(c => c.ControlPoint == point);

                        point.Changed -= updatePathTypes;
                    }

                    // If removing before the end of the path,
                    // update indices of connections after remove location
                    if (e.OldStartingIndex < Pieces.Count)
                    {
                        foreach (var connection in Connections)
                        {
                            if (connection.ControlPointIndex >= e.OldStartingIndex)
                                connection.ControlPointIndex -= e.OldItems.Count;
                        }
                    }

                    break;
            }
        }

        protected override bool OnClick(ClickEvent e)
        {
            foreach (var piece in Pieces)
            {
                piece.IsSelected.Value = false;
            }

            return false;
        }

        public bool OnPressed(PlatformAction action)
        {
            switch (action.ActionMethod)
            {
                case PlatformActionMethod.Delete:
                    return DeleteSelected();
            }

            return false;
        }

        public void OnReleased(PlatformAction action)
        {
        }

        /// <summary>
        /// Handles correction of invalid path types.
        /// </summary>
        private void updatePathTypes()
        {
            foreach (PathControlPoint segmentStartPoint in slider.Path.ControlPoints.Where(p => p.Type.Value != null))
            {
                if (segmentStartPoint.Type.Value != PathType.PerfectCurve)
                    continue;

                Vector2[] points = slider.Path.PointsInSegment(segmentStartPoint).Select(p => p.Position.Value).ToArray();
                if (points.Length == 3 && !validCircularArcSegment(points))
                    segmentStartPoint.Type.Value = PathType.Bezier;
            }
        }

        /// <summary>
        /// Returns whether the given points are arranged in a valid way. Invalid if points
        /// are almost entirely linear - as this causes the radius to approach infinity,
        /// which would exhaust memory when drawing / approximating.
        /// </summary>
        /// <param name="points">The three points that make up this circular arc segment.</param>
        /// <returns></returns>
        private bool validCircularArcSegment(IReadOnlyList<Vector2> points)
        {
            Vector2 a = points[0];
            Vector2 b = points[1];
            Vector2 c = points[2];

            float maxLength = points.Max(p => p.Length);

            Vector2 normA = new Vector2(a.X / maxLength, a.Y / maxLength);
            Vector2 normB = new Vector2(b.X / maxLength, b.Y / maxLength);
            Vector2 normC = new Vector2(c.X / maxLength, c.Y / maxLength);

            float det = (normA.X - normB.X) * (normB.Y - normC.Y) - (normB.X - normC.X) * (normA.Y - normB.Y);

            float acSq = (a - c).LengthSquared;
            float abSq = (a - b).LengthSquared;
            float bcSq = (b - c).LengthSquared;

            // Exterior = curve wraps around the long way between end-points
            // Exterior bottleneck is drawing-related, interior bottleneck is approximation-related,
            // where the latter is much faster, hence differing thresholds
            bool exterior = abSq > acSq || bcSq > acSq;
            float threshold = exterior ? 0.05f : 0.001f;

            return Math.Abs(det) >= threshold;
        }

        private void selectPiece(PathControlPointPiece piece, MouseButtonEvent e)
        {
            if (e.Button == MouseButton.Left && inputManager.CurrentState.Keyboard.ControlPressed)
                piece.IsSelected.Toggle();
            else
            {
                foreach (var p in Pieces)
                    p.IsSelected.Value = p == piece;
            }
        }

        [Resolved(CanBeNull = true)]
        private IEditorChangeHandler changeHandler { get; set; }

        public bool DeleteSelected()
        {
            List<PathControlPoint> toRemove = Pieces.Where(p => p.IsSelected.Value).Select(p => p.ControlPoint).ToList();

            // Ensure that there are any points to be deleted
            if (toRemove.Count == 0)
                return false;

            changeHandler?.BeginChange();
            RemoveControlPointsRequested?.Invoke(toRemove);
            changeHandler?.EndChange();

            // Since pieces are re-used, they will not point to the deleted control points while remaining selected
            foreach (var piece in Pieces)
                piece.IsSelected.Value = false;

            return true;
        }

        public MenuItem[] ContextMenuItems
        {
            get
            {
                if (!Pieces.Any(p => p.IsHovered))
                    return null;

                var selectedPieces = Pieces.Where(p => p.IsSelected.Value).ToList();
                int count = selectedPieces.Count;

                if (count == 0)
                    return null;

                List<MenuItem> items = new List<MenuItem>();

                if (!selectedPieces.Contains(Pieces[0]))
                    items.Add(createMenuItemForPathType(null));

                // todo: hide/disable items which aren't valid for selected points
                items.Add(createMenuItemForPathType(PathType.Linear));
                items.Add(createMenuItemForPathType(PathType.PerfectCurve));
                items.Add(createMenuItemForPathType(PathType.Bezier));
                items.Add(createMenuItemForPathType(PathType.Catmull));

                return new MenuItem[]
                {
                    new OsuMenuItem($"Delete {"control point".ToQuantity(count, count > 1 ? ShowQuantityAs.Numeric : ShowQuantityAs.None)}", MenuItemType.Destructive, () => DeleteSelected()),
                    new OsuMenuItem("Curve type")
                    {
                        Items = items
                    }
                };
            }
        }

        private MenuItem createMenuItemForPathType(PathType? type)
        {
            int totalCount = Pieces.Count(p => p.IsSelected.Value);
            int countOfState = Pieces.Where(p => p.IsSelected.Value).Count(p => p.ControlPoint.Type.Value == type);

            var item = new PathTypeMenuItem(type, () =>
            {
                foreach (var p in Pieces.Where(p => p.IsSelected.Value))
                    p.ControlPoint.Type.Value = type;
            });

            if (countOfState == totalCount)
                item.State.Value = TernaryState.True;
            else if (countOfState > 0)
                item.State.Value = TernaryState.Indeterminate;
            else
                item.State.Value = TernaryState.False;

            return item;
        }

        private class PathTypeMenuItem : TernaryStateMenuItem
        {
            public PathTypeMenuItem(PathType? type, Action action)
                : base(type == null ? "Inherit" : type.ToString().Humanize(), changeState, MenuItemType.Standard, _ => action?.Invoke())
            {
            }

            private static TernaryState changeState(TernaryState state) => TernaryState.True;
        }
    }
}
