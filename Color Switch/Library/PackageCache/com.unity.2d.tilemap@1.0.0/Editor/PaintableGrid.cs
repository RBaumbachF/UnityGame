using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace UnityEditor.Tilemaps
{
    internal abstract class PaintableGrid : ScriptableObject
    {
        private const int k_MaxMouseCellDelta = 500;

        public enum MarqueeType { None = 0, Pick, Box, Select }
        private int m_PermanentControlID;

        public abstract void Repaint();
        protected abstract void RegisterUndo();
        protected abstract void Paint(Vector3Int position);
        protected abstract void Erase(Vector3Int position);
        protected abstract void BoxFill(BoundsInt position);
        protected abstract void BoxErase(BoundsInt position);
        protected abstract void FloodFill(Vector3Int position);
        protected abstract void PickBrush(BoundsInt position, Vector3Int pickStart);
        protected abstract void Select(BoundsInt position);
        protected abstract void Move(BoundsInt from, BoundsInt to);
        protected abstract void MoveStart(BoundsInt position);
        protected abstract void MoveEnd(BoundsInt position);
        protected abstract bool ValidateFloodFillPosition(Vector3Int position);
        protected abstract Vector2Int ScreenToGrid(Vector2 screenPosition);
        protected abstract bool PickingIsDefaultTool();
        protected abstract bool CanPickOutsideEditMode();
        protected abstract Grid.CellLayout CellLayout();
        protected abstract void ClearGridSelection();

        protected virtual void OnBrushPickStarted() {}
        protected virtual void OnBrushPickDragged(BoundsInt position) {}

        protected virtual void OnBrushPickCancelled() {}

        protected virtual void OnEditStart() {}
        protected virtual void OnEditEnd() {}

        internal static PaintableGrid s_LastActivePaintableGrid;

        private Vector2Int m_PreviousMouseGridPosition;
        private Vector2Int m_MouseGridPosition;
        private bool m_MouseGridPositionChanged;
        private bool m_PositionChangeRepaintDone;
        protected Vector2Int? m_PreviousMove;
        protected Vector2Int? m_MarqueeStart;
        private MarqueeType m_MarqueeType = MarqueeType.None;
        private bool m_IsExecuting;
        private Type m_TypeBeforeExecution;
        private int m_ZPosition;

        public Vector2Int mouseGridPosition { get { return m_MouseGridPosition; } }
        public bool isPicking { get { return m_MarqueeType == MarqueeType.Pick; } }
        public bool isBoxing { get { return m_MarqueeType == MarqueeType.Box; } }
        public GridLayout.CellLayout cellLayout { get { return CellLayout(); } }
        public int zPosition { get { return m_ZPosition; } set { m_ZPosition = value; } }

        protected bool executing
        {
            get { return m_IsExecuting; }
            set
            {
                var isExecuting = value && isHotControl;
                if (isExecuting != m_IsExecuting)
                {
                    if (isExecuting)
                        OnEditStart();
                    else
                        OnEditEnd();
                }
                m_IsExecuting = isExecuting;
            }
        }

        protected bool isHotControl { get { return GUIUtility.hotControl == m_PermanentControlID; } }
        protected bool mouseGridPositionChanged { get { return m_MouseGridPositionChanged; } }
        protected bool inEditMode { get { return PaintableGrid.InGridEditMode(); } }

        protected virtual void OnEnable()
        {
            m_PermanentControlID = GUIUtility.GetPermanentControlID();
        }

        protected virtual void OnDisable()
        {
        }

        public virtual void OnGUI()
        {
            if (CanPickOutsideEditMode() || inEditMode)
                HandleBrushPicking();

            if (inEditMode)
            {
                HandleBrushPaintAndErase();
                HandleSelectTool();
                HandleMoveTool();
                HandleEditModeChange();
                HandleFloodFill();
                HandleBoxTool();
            }
            else if (isHotControl && !IsPickingEvent(Event.current))
            {
                // Release hot control if it still has it while not in picking or grid edit mode
                GUIUtility.hotControl = 0;
            }

            if (mouseGridPositionChanged && !m_PositionChangeRepaintDone)
            {
                Repaint();
                m_PositionChangeRepaintDone = true;
            }
        }

        protected void ResetPreviousMousePositionToCurrentPosition()
        {
            m_PreviousMouseGridPosition = m_MouseGridPosition;
        }

        protected void UpdateMouseGridPosition(bool forceUpdate = false)
        {
            if (Event.current.type == EventType.MouseDrag
                || Event.current.type == EventType.MouseMove
                // Case 1075857: Mouse Down when window is not in focus needs to update mouse grid position
                || Event.current.type == EventType.MouseDown
                || Event.current.type == EventType.DragUpdated
                || forceUpdate)
            {
                Vector2Int newGridPosition = ScreenToGrid(Event.current.mousePosition);
                if (newGridPosition != m_MouseGridPosition)
                {
                    var delta = newGridPosition - m_MouseGridPosition;
                    // Case 1024422: Limit mouse cell delta changes for Grid/Tilemap input handling due to camera changes when switching modes/axis views
                    if (Mathf.Abs(delta.x) > k_MaxMouseCellDelta)
                        newGridPosition.x = m_MouseGridPosition.x + Math.Sign(delta.x) * k_MaxMouseCellDelta;
                    if (Mathf.Abs(delta.y) > k_MaxMouseCellDelta)
                        newGridPosition.y = m_MouseGridPosition.y + Math.Sign(delta.y) * k_MaxMouseCellDelta;
                    ResetPreviousMousePositionToCurrentPosition();
                    m_MouseGridPosition = newGridPosition;
                    MouseGridPositionChanged();
                }
                else if (!forceUpdate)
                {
                    m_MouseGridPositionChanged = false;
                }
            }
        }

        private void MouseGridPositionChanged()
        {
            m_MouseGridPositionChanged = true;
            m_PositionChangeRepaintDone = false;
        }

        private void HandleEditModeChange()
        {
            // Handles changes in EditMode while tool is expected to be in the same mode
            if (isPicking && !TilemapEditorTool.IsActive(typeof(PickingTool)))
            {
                m_MarqueeStart = null;
                m_MarqueeType = MarqueeType.None;
                if (isHotControl)
                {
                    GUI.changed = true;
                    GUIUtility.hotControl = 0;
                }
            }
            if (isBoxing && !TilemapEditorTool.IsActive(typeof(BoxTool)))
            {
                m_MarqueeStart = null;
                m_MarqueeType = MarqueeType.None;
                if (isHotControl)
                {
                    GUI.changed = true;
                    GUIUtility.hotControl = 0;
                }
            }
            if (!TilemapEditorTool.IsActive(typeof(SelectTool)) && !TilemapEditorTool.IsActive(typeof(MoveTool)))
            {
                ClearGridSelection();
            }
        }

        private void HandleBrushPicking()
        {
            Event evt = Event.current;

            if (evt.type == EventType.MouseDown && IsPickingEvent(evt) && !isHotControl)
            {
                m_TypeBeforeExecution = typeof(PaintTool);
                if (inEditMode && !TilemapEditorTool.IsActive(typeof(PickingTool)))
                {
                    m_TypeBeforeExecution = UnityEditor.EditorTools.ToolManager.activeToolType;
                    TilemapEditorTool.SetActiveEditorTool(typeof(PickingTool));
                }

                m_MarqueeStart = mouseGridPosition;
                m_MarqueeType = MarqueeType.Pick;
                s_LastActivePaintableGrid = this;
                Event.current.Use();
                GUI.changed = true;
                GUIUtility.hotControl = m_PermanentControlID;
                OnBrushPickStarted();
            }
            if (evt.type == EventType.MouseDrag && isHotControl && m_MarqueeStart.HasValue && m_MarqueeType == MarqueeType.Pick && IsPickingEvent(evt))
            {
                RectInt rect = GridEditorUtility.GetMarqueeRect(m_MarqueeStart.Value, mouseGridPosition);
                OnBrushPickDragged(new BoundsInt(new Vector3Int(rect.xMin, rect.yMin, zPosition), new Vector3Int(rect.size.x, rect.size.y, 1)));
                Event.current.Use();
                GUI.changed = true;
            }
            if (evt.rawType == EventType.MouseUp && isHotControl && m_MarqueeStart.HasValue && m_MarqueeType == MarqueeType.Pick && IsPickingEvent(evt))
            {
                // Check if event only occurred in the PaintableGrid window as evt.type will filter for this
                if (evt.type == EventType.MouseUp && m_MarqueeType == MarqueeType.Pick)
                {
                    RectInt rect = GridEditorUtility.GetMarqueeRect(m_MarqueeStart.Value, mouseGridPosition);
                    Vector2Int pivot = GetMarqueePivot(m_MarqueeStart.Value, mouseGridPosition);
                    PickBrush(new BoundsInt(new Vector3Int(rect.xMin, rect.yMin, zPosition), new Vector3Int(rect.size.x, rect.size.y, 1)), new Vector3Int(pivot.x, pivot.y, 0));

                    if (inEditMode && UnityEditor.EditorTools.ToolManager.activeToolType != m_TypeBeforeExecution)
                    {
                        if (PickingIsDefaultTool()
                            && (m_TypeBeforeExecution == typeof(EraseTool)
                                || m_TypeBeforeExecution == typeof(MoveTool)))
                        {
                            // If Picking is default, change to a Paint Tool to facilitate editing if previous tool does not allow for painting
                            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));
                        }
                        else
                        {
                            TilemapEditorTool.SetActiveEditorTool(m_TypeBeforeExecution);
                        }
                    }

                    GridPaletteBrushes.ActiveGridBrushAssetChanged();
                    s_LastActivePaintableGrid = this;
                    Event.current.Use();
                    GUI.changed = true;
                }
                else
                // Event occurred outside of PaintableGrid window, cancel the pick event
                {
                    OnBrushPickCancelled();
                }
                m_MarqueeType = MarqueeType.None;
                m_MarqueeStart = null;
                GUIUtility.hotControl = 0;
                InspectorWindow.RepaintAllInspectors();
            }
        }

        private bool IsPickingEvent(Event evt)
        {
            return ((evt.control && !TilemapEditorTool.IsActive(typeof(MoveTool)))
                || TilemapEditorTool.IsActive(typeof(PickingTool))
                || !TilemapEditorTool.IsActive(typeof(SelectTool)) && PickingIsDefaultTool())
                && evt.button == 0 && !evt.alt;
        }

        private void HandleSelectTool()
        {
            Event evt = Event.current;

            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt && (TilemapEditorTool.IsActive(typeof(SelectTool)) || (TilemapEditorTool.IsActive(typeof(MoveTool)) && evt.control)))
            {
                if (TilemapEditorTool.IsActive(typeof(MoveTool)) && evt.control)
                    TilemapEditorTool.SetActiveEditorTool(typeof(SelectTool));

                m_PreviousMove = null;
                m_MarqueeStart = mouseGridPosition;
                m_MarqueeType = MarqueeType.Select;

                s_LastActivePaintableGrid = this;
                GUIUtility.hotControl = m_PermanentControlID;
                Event.current.Use();
            }
            if (evt.rawType == EventType.MouseUp && evt.button == 0 && !evt.alt && m_MarqueeStart.HasValue && isHotControl && TilemapEditorTool.IsActive(typeof(SelectTool)))
            {
                // Check if event only occurred in the PaintableGrid window as evt.type will filter for this
                if (evt.type == EventType.MouseUp && m_MarqueeType == MarqueeType.Select)
                {
                    RectInt rect = GridEditorUtility.GetMarqueeRect(m_MarqueeStart.Value, mouseGridPosition);
                    Select(new BoundsInt(new Vector3Int(rect.xMin, rect.yMin, zPosition), new Vector3Int(rect.size.x, rect.size.y, 1)));
                    Event.current.Use();
                }
                if (evt.control)
                    TilemapEditorTool.SetActiveEditorTool(typeof(MoveTool));
                m_MarqueeStart = null;
                m_MarqueeType = MarqueeType.None;
                InspectorWindow.RepaintAllInspectors();
                GUIUtility.hotControl = 0;
            }
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && !m_MarqueeStart.HasValue && !m_PreviousMove.HasValue)
            {
                ClearGridSelection();
                Event.current.Use();
            }
        }

        private void HandleMoveTool()
        {
            Event evt = Event.current;

            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt && TilemapEditorTool.IsActive(typeof(MoveTool)))
            {
                RegisterUndo();
                Vector3Int mouse3D = new Vector3Int(mouseGridPosition.x, mouseGridPosition.y, GridSelection.position.zMin);
                if (GridSelection.active && GridSelection.position.Contains(mouse3D))
                {
                    GUIUtility.hotControl = m_PermanentControlID;
                    executing = true;
                    m_MarqueeStart = null;
                    m_MarqueeType = MarqueeType.None;
                    m_PreviousMove = mouseGridPosition;
                    MoveStart(GridSelection.position);
                    s_LastActivePaintableGrid = this;
                }
                Event.current.Use();
            }
            if (evt.type == EventType.MouseDrag && evt.button == 0 && TilemapEditorTool.IsActive(typeof(MoveTool)) && isHotControl)
            {
                if (m_MouseGridPositionChanged && m_PreviousMove.HasValue)
                {
                    executing = true;
                    BoundsInt previousRect = GridSelection.position;
                    BoundsInt previousBounds = new BoundsInt(new Vector3Int(previousRect.xMin, previousRect.yMin, GridSelection.position.zMin), new Vector3Int(previousRect.size.x, previousRect.size.y, 1));

                    Vector2Int direction = mouseGridPosition - m_PreviousMove.Value;
                    BoundsInt pos = GridSelection.position;
                    pos.position = new Vector3Int(pos.x + direction.x, pos.y + direction.y, pos.z);
                    GridSelection.position = pos;
                    Move(previousBounds, pos);
                    m_PreviousMove = mouseGridPosition;
                    Event.current.Use();
                }
            }
            if (evt.type == EventType.MouseUp && evt.button == 0 && m_PreviousMove.HasValue && TilemapEditorTool.IsActive(typeof(MoveTool)) && isHotControl)
            {
                m_PreviousMove = null;
                MoveEnd(GridSelection.position);
                executing = false;
                GUIUtility.hotControl = 0;
                Event.current.Use();
            }
        }

        private void HandleBrushPaintAndErase()
        {
            Event evt = Event.current;
            if (!IsPaintingEvent(evt) && !IsErasingEvent(evt))
                return;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    RegisterUndo();
                    GUIUtility.hotControl = m_PermanentControlID;
                    executing = true;
                    m_TypeBeforeExecution = EditorTools.ToolManager.activeToolType;
                    if (IsErasingEvent(evt))
                    {
                        if (!TilemapEditorTool.IsActive(typeof(EraseTool)))
                            TilemapEditorTool.SetActiveEditorTool(typeof(EraseTool));
                        Erase(new Vector3Int(mouseGridPosition.x, mouseGridPosition.y, zPosition));
                    }
                    else
                    {
                        if (!TilemapEditorTool.IsActive(typeof(PaintTool)))
                            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));
                        Paint(new Vector3Int(mouseGridPosition.x, mouseGridPosition.y, zPosition));
                    }
                    Event.current.Use();
                    GUI.changed = true;
                    break;
                case EventType.MouseDrag:
                    executing = true;
                    if (isHotControl && mouseGridPositionChanged)
                    {
                        List<Vector2Int> points = GridEditorUtility.GetPointsOnLine(m_PreviousMouseGridPosition, mouseGridPosition).ToList();
                        if (points[0] == mouseGridPosition)
                            points.Reverse();

                        if (!evt.shift && !TilemapEditorTool.IsActive(typeof(PaintTool)) && m_TypeBeforeExecution == typeof(PaintTool))
                            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));
                        else if (evt.shift && TilemapEditorTool.IsActive(typeof(PaintTool)))
                            TilemapEditorTool.SetActiveEditorTool(typeof(EraseTool));

                        for (int i = 1; i < points.Count; i++)
                        {
                            if (IsErasingEvent(evt))
                            {
                                Erase(new Vector3Int(points[i].x, points[i].y, zPosition));
                            }
                            else
                            {
                                Paint(new Vector3Int(points[i].x, points[i].y, zPosition));
                            }
                        }
                        Event.current.Use();
                        GUI.changed = true;
                    }
                    break;
                case EventType.MouseUp:
                    executing = false;
                    if (isHotControl)
                    {
                        if (!TilemapEditorTool.IsActive(typeof(PaintTool)) && m_TypeBeforeExecution == typeof(PaintTool))
                        {
                            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));
                        }

                        Event.current.Use();
                        GUI.changed = true;
                        GUIUtility.hotControl = 0;
                    }
                    break;
            }
        }

        private bool IsPaintingEvent(Event evt)
        {
            return (evt.button == 0 && !evt.control && !evt.alt && TilemapEditorTool.IsActive(typeof(PaintTool)));
        }

        private bool IsErasingEvent(Event evt)
        {
            return (evt.button == 0 && !evt.control && !evt.alt
                && ((evt.shift && !TilemapEditorTool.IsActive(typeof(BoxTool))
                    && !TilemapEditorTool.IsActive(typeof(FillTool))
                    && !TilemapEditorTool.IsActive(typeof(SelectTool))
                    && !TilemapEditorTool.IsActive(typeof(MoveTool)))
                    || TilemapEditorTool.IsActive(typeof(EraseTool))));
        }

        private void HandleFloodFill()
        {
            if (TilemapEditorTool.IsActive(typeof(FillTool)) && GridPaintingState.gridBrush != null && ValidateFloodFillPosition(new Vector3Int(mouseGridPosition.x, mouseGridPosition.y, 0)))
            {
                Event evt = Event.current;
                if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
                {
                    GUIUtility.hotControl = m_PermanentControlID;
                    GUI.changed = true;
                    executing = true;
                    Event.current.Use();
                }
                if (evt.type == EventType.MouseUp && evt.button == 0 && isHotControl)
                {
                    RegisterUndo();
                    FloodFill(new Vector3Int(mouseGridPosition.x, mouseGridPosition.y, zPosition));
                    executing = false;
                    GUI.changed = true;
                    Event.current.Use();
                    GUIUtility.hotControl = 0;
                }
            }
        }

        private void HandleBoxTool()
        {
            Event evt = Event.current;

            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt && TilemapEditorTool.IsActive(typeof(BoxTool)))
            {
                m_MarqueeStart = mouseGridPosition;
                m_MarqueeType = MarqueeType.Box;
                Event.current.Use();
                GUI.changed = true;
                executing = true;
                GUIUtility.hotControl = m_PermanentControlID;
            }
            if (evt.type == EventType.MouseDrag && evt.button == 0 && TilemapEditorTool.IsActive(typeof(BoxTool)))
            {
                if (isHotControl && m_MarqueeStart.HasValue)
                {
                    Event.current.Use();
                    executing = true;
                    GUI.changed = true;
                }
            }
            if (evt.type == EventType.MouseUp && evt.button == 0 && TilemapEditorTool.IsActive(typeof(BoxTool)))
            {
                if (isHotControl && m_MarqueeStart.HasValue)
                {
                    RegisterUndo();
                    RectInt rect = GridEditorUtility.GetMarqueeRect(m_MarqueeStart.Value, mouseGridPosition);
                    if (evt.shift)
                        BoxErase(new BoundsInt(rect.x, rect.y, zPosition, rect.size.x, rect.size.y, 1));
                    else
                        BoxFill(new BoundsInt(rect.x, rect.y, zPosition, rect.size.x, rect.size.y, 1));
                    Event.current.Use();
                    executing = false;
                    GUI.changed = true;
                    GUIUtility.hotControl = 0;
                }
                m_MarqueeStart = null;
                m_MarqueeType = MarqueeType.None;
            }
        }

        private Vector2Int GetMarqueePivot(Vector2Int start, Vector2Int end)
        {
            Vector2Int pivot = new Vector2Int(
                Math.Max(end.x - start.x, 0),
                Math.Max(end.y - start.y, 0)
            );
            return pivot;
        }

        public void ChangeZPosition(int change)
        {
            m_ZPosition += change;
            MouseGridPositionChanged();
            Repaint();
        }

        public void ResetZPosition()
        {
            if (m_ZPosition == 0)
                return;

            m_ZPosition = 0;
            MouseGridPositionChanged();
            Repaint();
        }

        public static bool InGridEditMode()
        {
            return UnityEditor.EditorTools.ToolManager.activeToolType != null
                && UnityEditor.EditorTools.ToolManager.activeToolType.IsSubclassOf(typeof(TilemapEditorTool));
        }

        // TODO: Someday EditMode or its future incarnation will be public and we can get rid of this
        // TODO: Temporarily use ActiveTool's type to determine brush tool
        public static GridBrushBase.Tool EditTypeToBrushTool(Type activeToolType)
        {
            if (activeToolType == typeof(BoxTool))
                return GridBrushBase.Tool.Box;
            if (activeToolType == typeof(EraseTool))
                return GridBrushBase.Tool.Erase;
            if (activeToolType == typeof(FillTool))
                return GridBrushBase.Tool.FloodFill;
            if (activeToolType == typeof(PaintTool))
                return GridBrushBase.Tool.Paint;
            if (activeToolType == typeof(PickingTool))
                return GridBrushBase.Tool.Pick;
            if (activeToolType == typeof(SelectTool))
                return GridBrushBase.Tool.Select;
            if (activeToolType == typeof(MoveTool))
                return GridBrushBase.Tool.Move;
            return GridBrushBase.Tool.Paint;
        }
    }
}
