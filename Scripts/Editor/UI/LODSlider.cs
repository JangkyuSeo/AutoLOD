﻿using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.AutoLOD
{
    public class LODSlider : ScriptableObject
    {
        public readonly Color[] kLODColors =
        {
            new Color(0.4831376f, 0.6211768f, 0.0219608f, 1.0f),
            new Color(0.2792160f, 0.4078432f, 0.5835296f, 1.0f),
            new Color(0.2070592f, 0.5333336f, 0.6556864f, 1.0f),
            new Color(0.5333336f, 0.1600000f, 0.0282352f, 1.0f),
            new Color(0.3827448f, 0.2886272f, 0.5239216f, 1.0f),
            new Color(0.8000000f, 0.4423528f, 0.0000000f, 1.0f),
            new Color(0.4486272f, 0.4078432f, 0.0501960f, 1.0f),
            new Color(0.7749016f, 0.6368624f, 0.0250984f, 1.0f)
        };

        public static readonly Color kDefaultLODColor = new Color(.4f, 0f, 0f, 1f);

        class GUIStyles
        {
            public readonly GUIStyle LODSliderBG = "LODSliderBG";
            

            public GUIStyles()
            {

            }
        }

        private static GUIStyles s_Styles;

        private  const int k_SliderBarHeight = 30;
        private int m_SliderID = typeof(LODSlider).GetHashCode();

        private int m_SelectedIndex = -1;
        private LODSliderRange m_DefaultRange = null;

        [SerializeField]
        private List<LODSliderRange> m_RangeList = new List<LODSliderRange>();

        private SerializedObject serialized;

        private static GUIStyles Styles
        {
            get
            {
                if (s_Styles == null)
                    s_Styles = new GUIStyles();
                return s_Styles;
            }
        }

        public LODSlider(bool useDefault = false, string name = "")
        {
            serialized = new SerializedObject(this);
            if (useDefault == true)
            {
                var defaultRange = new LODSliderRange();
                defaultRange.Name = name;
                defaultRange.EndPosition = 0.0f;
            }
        }

        public void InsertRange(string name, float endPosition)
        {
            if (m_DefaultRange == null && m_RangeList.Count == 0)
            {
                endPosition = 0.0f;
            }

            var range = new LODSliderRange();
            range.Name = name;
            range.EndPosition = endPosition;

            int insertPosition = 0;

            for (; insertPosition < m_RangeList.Count; ++insertPosition)
            {
                if (m_RangeList[insertPosition].EndPosition < endPosition)
                {
                    break;
                }
            }

            m_RangeList.Insert(insertPosition, range);
        }

        public float GetRangeValue(string name)
        {
            for (int i = 0; i < m_RangeList.Count; ++i)
            {
                if (m_RangeList[i].Name == name)
                    return m_RangeList[i].EndPosition;
            }

            return 1.0f;
        }

        public int GetRangeCount()
        {
            return m_RangeList.Count;
        }


        public void Draw()
        {
            serialized.Update();

            var sliderBarPosition = GUILayoutUtility.GetRect(0, k_SliderBarHeight, GUILayout.ExpandWidth(true));
            sliderBarPosition.width -= 5;   //< for margin

            Event evt = Event.current;
            int sliderId = GUIUtility.GetControlID(m_SliderID, FocusType.Passive);

            switch (evt.GetTypeForControl(sliderId))
            {
                case EventType.Repaint:
                {
                    Styles.LODSliderBG.Draw(sliderBarPosition, GUIContent.none, false, false, false, false);

                    float startPosition = 1.0f;
                    for (int i = 0; i < m_RangeList.Count; ++i)
                    {
                        m_RangeList[i].Draw(sliderBarPosition, kLODColors[i], startPosition);
                        //if default range has not existed then last range should not be drawn.
                        if (i != m_RangeList.Count -1 || m_DefaultRange != null )
                            m_RangeList[i].DrawCursor(sliderBarPosition);
                        startPosition = m_RangeList[i].EndPosition;
                    }

                    if (m_DefaultRange != null)
                    {
                        m_DefaultRange.Draw(sliderBarPosition, kDefaultLODColor, startPosition);
                    }
                    break;
                }

                case EventType.MouseDown:
                {
                    int count = m_RangeList.Count;
                    if (m_DefaultRange == null)
                        count -= 1;

                    for (int i = 0; i < count; ++i)
                    {
                        Rect resizeArea = m_RangeList[i].GetResizeArea(sliderBarPosition);

                        if (resizeArea.Contains(evt.mousePosition) == true)
                        {
                            evt.Use();
                            GUIUtility.hotControl = sliderId;
                            m_SelectedIndex = i;
                            break;
                        }
                    }

                    break;
                }
                case EventType.MouseDrag:
                {
                    
                    if (GUIUtility.hotControl == sliderId && m_SelectedIndex >= 0)
                    {
                        evt.Use();

                        var percentage = 1.0f - Mathf.Clamp((evt.mousePosition.x - sliderBarPosition.x) / sliderBarPosition.width, 0.01f, 1.0f);
                        percentage = (percentage * percentage);
   
                        var property = serialized.FindProperty(string.Format("m_RangeList.Array.data[{0}].EndPosition", m_SelectedIndex));
                        property.floatValue = percentage;
                    }
                    break;
                }
                case EventType.MouseUp:
                {
                    if (GUIUtility.hotControl == sliderId)
                    {
                        evt.Use();
                        m_SelectedIndex = -1;
                        GUIUtility.hotControl = 0;
                    }
                    break;
                }
            }

            serialized.ApplyModifiedProperties();
        }

    }

}