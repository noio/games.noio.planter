using UnityEngine;
using UnityEngine.UIElements;

namespace games.noio.planter.Editor
{
    public class CircleFillElement : VisualElement
    {
        public CircleFillElement()
        {
            // style.height = radius * 2;
            // style.width = radius * 2;
            generateVisualContent += GenerateVisualContent;
        }

        #region PROPERTIES

        public float Arc { get; set; } = 30;

        public Gradient lineGradient => new()
        {
            colorKeys = new[]
            {
                new(Color.grey, 0),
                new GradientColorKey(Color.white, 1)
            },
            alphaKeys = new[]
            {
                new GradientAlphaKey(0, 0),
                new GradientAlphaKey(1, 1)
            }
        };

        #endregion

        void GenerateVisualContent(MeshGenerationContext ctx)
        {
            var radius = Mathf.Min(resolvedStyle.width, resolvedStyle.height) / 2;

            var painter = ctx.painter2D;
            painter.strokeColor = new Color(0.8f, 0.8f, 0.8f);
            var lineWidth =  painter.lineWidth = radius * .65f;

            var arc = Mathf.Max(2, Arc);
            painter.BeginPath();
            var center = new Vector2(radius, radius);
            painter.Arc(center, radius - lineWidth / 2, 0, arc);
            painter.Stroke();
            painter.BeginPath();
            painter.Arc(center, radius - lineWidth / 2, 0, -arc,
                ArcDirection.CounterClockwise);
            painter.Stroke();

            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.Arc(center, radius, 0, 360);
            painter.Stroke();
        }

        public new class UxmlFactory : UxmlFactory<CircleFillElement>
        {
        }
    }
}