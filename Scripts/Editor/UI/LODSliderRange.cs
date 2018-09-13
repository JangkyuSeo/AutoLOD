﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


namespace Unity.AutoLOD
{
    [Serializable]
    class LODSliderRange
    {
        class GUIStyles
        {
            public readonly GUIStyle LODSliderRange = "LODSliderRange";
            public readonly GUIStyle LODSliderText = "LODSliderText";

            public GUIStyles()
            {

            }
        }

        private static GUIStyles s_Styles;

        private static GUIStyles Styles
        {
            get
            {
                if (s_Styles == null)
                    s_Styles = new GUIStyles();
                return s_Styles;
            }
        }

        [SerializeField]
        public string Name;
        [SerializeField]
        public float EndPosition;


        public Rect GetResizeArea(Rect sliderArea)
        {
            float pos = sliderArea.width * (1.0f - Mathf.Sqrt(EndPosition));
            return new Rect(sliderArea.x + pos - 5.0f, sliderArea.y, 10.0f, sliderArea.height );
        }
        public void Draw(Rect sliderArea, Color color, float startPosition)
        {
            var tempColor = GUI.backgroundColor;
            var startPercentageString = string.Format("{0}\n{1:0}%", Name, startPosition * 100.0f );

            GUI.backgroundColor = color;
            var startX = Mathf.Round(sliderArea.width * (1.0f- Mathf.Sqrt(startPosition)));
            var endX = Mathf.Round(sliderArea.width * (1.0f - Mathf.Sqrt(EndPosition)));

            var rect = new Rect(sliderArea.x + startX, sliderArea.y, endX - startX, sliderArea.height);

            Styles.LODSliderRange.Draw(rect, GUIContent.none, false, false, false, false);
            Styles.LODSliderText.Draw(rect, startPercentageString, false, false, false, false);
            GUI.backgroundColor = tempColor;
        }

        public void DrawCursor(Rect sliderArea)
        {
            EditorGUIUtility.AddCursorRect(GetResizeArea(sliderArea), MouseCursor.ResizeHorizontal);
        }
    }
}