﻿using System;
using System.Collections.Generic;

namespace LottieUWP
{
    internal class PolystarContent : IPathContent, BaseKeyframeAnimation.IAnimationListener
    {
        /// <summary>
        /// This was empirically derived by creating polystars, converting them to
        /// curves, and calculating a scale factor.
        /// It works best for polygons and stars with 3 points and needs more
        /// work otherwise.
        /// </summary>
        private const float PolystarMagicNumber = .47829f;
        private const float PolygonMagicNumber = .25f;
        private readonly Path _path = new Path();

        private readonly LottieDrawable _lottieDrawable;
        private readonly PolystarShape.Type _type;
        private readonly IBaseKeyframeAnimation<float?> _pointsAnimation;
        private readonly IBaseKeyframeAnimation<PointF> _positionAnimation;
        private readonly IBaseKeyframeAnimation<float?> _rotationAnimation;
        private readonly IBaseKeyframeAnimation<float?> _innerRadiusAnimation;
        private readonly IBaseKeyframeAnimation<float?> _outerRadiusAnimation;
        private readonly IBaseKeyframeAnimation<float?> _innerRoundednessAnimation;
        private readonly IBaseKeyframeAnimation<float?> _outerRoundednessAnimation;

        private TrimPathContent _trimPath;
        private bool _isPathValid;

        internal PolystarContent(LottieDrawable lottieDrawable, BaseLayer layer, PolystarShape polystarShape)
        {
            _lottieDrawable = lottieDrawable;

            Name = polystarShape.Name;
            _type = polystarShape.getType();
            _pointsAnimation = polystarShape.Points.CreateAnimation();
            _positionAnimation = polystarShape.Position.CreateAnimation();
            _rotationAnimation = polystarShape.Rotation.CreateAnimation();
            _outerRadiusAnimation = polystarShape.OuterRadius.CreateAnimation();
            _outerRoundednessAnimation = polystarShape.OuterRoundedness.CreateAnimation();
            if (_type == PolystarShape.Type.Star)
            {
                _innerRadiusAnimation = polystarShape.InnerRadius.CreateAnimation();
                _innerRoundednessAnimation = polystarShape.InnerRoundedness.CreateAnimation();
            }
            else
            {
                _innerRadiusAnimation = null;
                _innerRoundednessAnimation = null;
            }

            layer.AddAnimation(_pointsAnimation);
            layer.AddAnimation(_positionAnimation);
            layer.AddAnimation(_rotationAnimation);
            layer.AddAnimation(_outerRadiusAnimation);
            layer.AddAnimation(_outerRoundednessAnimation);
            if (_type == PolystarShape.Type.Star)
            {
                layer.AddAnimation(_innerRadiusAnimation);
                layer.AddAnimation(_innerRoundednessAnimation);
            }

            _pointsAnimation.AddUpdateListener(this);
            _positionAnimation.AddUpdateListener(this);
            _rotationAnimation.AddUpdateListener(this);
            _outerRadiusAnimation.AddUpdateListener(this);
            _outerRoundednessAnimation.AddUpdateListener(this);
            if (_type == PolystarShape.Type.Star)
            {
                _outerRadiusAnimation.AddUpdateListener(this);
                _outerRoundednessAnimation.AddUpdateListener(this);
            }
        }

        public void OnValueChanged()
        {
            Invalidate();
        }

        private void Invalidate()
        {
            _isPathValid = false;
            _lottieDrawable.InvalidateSelf();
        }

        public void SetContents(IList<IContent> contentsBefore, IList<IContent> contentsAfter)
        {
            for (int i = 0; i < contentsBefore.Count; i++)
            {
                var trimPathContent = contentsBefore[i] as TrimPathContent;
                if (trimPathContent != null && trimPathContent.Type == ShapeTrimPath.Type.Simultaneously)
                {
                    _trimPath = trimPathContent;
                    _trimPath.AddListener(this);
                }
            }
        }

        public virtual Path Path
        {
            get
            {
                if (_isPathValid)
                {
                    return _path;
                }

                _path.Reset();

                switch (_type.InnerEnumValue)
                {
                    case PolystarShape.Type.InnerEnum.Star:
                        CreateStarPath();
                        break;
                    case PolystarShape.Type.InnerEnum.Polygon:
                        CreatePolygonPath();
                        break;
                }

                _path.Close();

                Utils.ApplyTrimPathIfNeeded(_path, _trimPath);

                _isPathValid = true;
                return _path;
            }
        }

        public string Name { get; }

        private void CreateStarPath()
        {
            float points = _pointsAnimation.Value.Value;
            double currentAngle = _rotationAnimation?.Value ?? 0f;
            // Start at +y instead of +x
            currentAngle -= 90;
            // convert to radians
            currentAngle = MathExt.ToRadians(currentAngle);
            // adjust current angle for partial points
            float anglePerPoint = (float)(2 * Math.PI / points);
            float halfAnglePerPoint = anglePerPoint / 2.0f;
            float partialPointAmount = points - (int)points;
            if (partialPointAmount != 0)
            {
                currentAngle += halfAnglePerPoint * (1f - partialPointAmount);
            }

            float outerRadius = _outerRadiusAnimation.Value.Value;
            //noinspection ConstantConditions
            float innerRadius = _innerRadiusAnimation.Value.Value;

            float innerRoundedness = 0f;
            if (_innerRoundednessAnimation != null)
            {
                innerRoundedness = _innerRoundednessAnimation.Value.Value / 100f;
            }
            float outerRoundedness = 0f;
            if (_outerRoundednessAnimation != null)
            {
                outerRoundedness = _outerRoundednessAnimation.Value.Value / 100f;
            }

            float x;
            float y;
            float partialPointRadius = 0;
            if (partialPointAmount != 0)
            {
                partialPointRadius = innerRadius + partialPointAmount * (outerRadius - innerRadius);
                x = (float)(partialPointRadius * Math.Cos(currentAngle));
                y = (float)(partialPointRadius * Math.Sin(currentAngle));
                _path.MoveTo(x, y);
                currentAngle += anglePerPoint * partialPointAmount / 2f;
            }
            else
            {
                x = (float)(outerRadius * Math.Cos(currentAngle));
                y = (float)(outerRadius * Math.Sin(currentAngle));
                _path.MoveTo(x, y);
                currentAngle += halfAnglePerPoint;
            }

            // True means the line will go to outer radius. False means inner radius.
            bool longSegment = false;
            double numPoints = Math.Ceiling(points) * 2;
            for (int i = 0; i < numPoints; i++)
            {
                float radius = longSegment ? outerRadius : innerRadius;
                float dTheta = halfAnglePerPoint;
                if (partialPointRadius != 0 && i == numPoints - 2)
                {
                    dTheta = anglePerPoint * partialPointAmount / 2f;
                }
                if (partialPointRadius != 0 && i == numPoints - 1)
                {
                    radius = partialPointRadius;
                }
                var previousX = x;
                var previousY = y;
                x = (float)(radius * Math.Cos(currentAngle));
                y = (float)(radius * Math.Sin(currentAngle));

                if (innerRoundedness == 0 && outerRoundedness == 0)
                {
                    _path.LineTo(x, y);
                }
                else
                {
                    float cp1Theta = (float)(Math.Atan2(previousY, previousX) - Math.PI / 2f);
                    float cp1Dx = (float)Math.Cos(cp1Theta);
                    float cp1Dy = (float)Math.Sin(cp1Theta);

                    float cp2Theta = (float)(Math.Atan2(y, x) - Math.PI / 2f);
                    float cp2Dx = (float)Math.Cos(cp2Theta);
                    float cp2Dy = (float)Math.Sin(cp2Theta);

                    float cp1Roundedness = longSegment ? innerRoundedness : outerRoundedness;
                    float cp2Roundedness = longSegment ? outerRoundedness : innerRoundedness;
                    float cp1Radius = longSegment ? innerRadius : outerRadius;
                    float cp2Radius = longSegment ? outerRadius : innerRadius;

                    float cp1X = cp1Radius * cp1Roundedness * PolystarMagicNumber * cp1Dx;
                    float cp1Y = cp1Radius * cp1Roundedness * PolystarMagicNumber * cp1Dy;
                    float cp2X = cp2Radius * cp2Roundedness * PolystarMagicNumber * cp2Dx;
                    float cp2Y = cp2Radius * cp2Roundedness * PolystarMagicNumber * cp2Dy;
                    if (partialPointAmount != 0)
                    {
                        if (i == 0)
                        {
                            cp1X *= partialPointAmount;
                            cp1Y *= partialPointAmount;
                        }
                        else if (i == numPoints - 1)
                        {
                            cp2X *= partialPointAmount;
                            cp2Y *= partialPointAmount;
                        }
                    }

                    _path.CubicTo(previousX - cp1X, previousY - cp1Y, x + cp2X, y + cp2Y, x, y);
                }

                currentAngle += dTheta;
                longSegment = !longSegment;
            }


            PointF position = _positionAnimation.Value;
            _path.Offset(position.X, position.Y);
            _path.Close();
        }

        private void CreatePolygonPath()
        {
            float points = (float)Math.Floor(_pointsAnimation.Value.Value);
            double currentAngle = _rotationAnimation?.Value ?? 0f;
            // Start at +y instead of +x
            currentAngle -= 90;
            // convert to radians
            currentAngle = MathExt.ToRadians(currentAngle);
            // adjust current angle for partial points
            float anglePerPoint = (float)(2 * Math.PI / points);

            float roundedness = _outerRoundednessAnimation.Value.Value / 100f;
            float radius = _outerRadiusAnimation.Value.Value;
            float x;
            float y;
            x = (float)(radius * Math.Cos(currentAngle));
            y = (float)(radius * Math.Sin(currentAngle));
            _path.MoveTo(x, y);
            currentAngle += anglePerPoint;

            double numPoints = Math.Ceiling(points);
            for (int i = 0; i < numPoints; i++)
            {
                var previousX = x;
                var previousY = y;
                x = (float)(radius * Math.Cos(currentAngle));
                y = (float)(radius * Math.Sin(currentAngle));

                if (roundedness != 0)
                {
                    float cp1Theta = (float)(Math.Atan2(previousY, previousX) - Math.PI / 2f);
                    float cp1Dx = (float)Math.Cos(cp1Theta);
                    float cp1Dy = (float)Math.Sin(cp1Theta);

                    float cp2Theta = (float)(Math.Atan2(y, x) - Math.PI / 2f);
                    float cp2Dx = (float)Math.Cos(cp2Theta);
                    float cp2Dy = (float)Math.Sin(cp2Theta);

                    float cp1X = radius * roundedness * PolygonMagicNumber * cp1Dx;
                    float cp1Y = radius * roundedness * PolygonMagicNumber * cp1Dy;
                    float cp2X = radius * roundedness * PolygonMagicNumber * cp2Dx;
                    float cp2Y = radius * roundedness * PolygonMagicNumber * cp2Dy;
                    _path.CubicTo(previousX - cp1X, previousY - cp1Y, x + cp2X, y + cp2Y, x, y);
                }
                else
                {
                    _path.LineTo(x, y);
                }

                currentAngle += anglePerPoint;
            }

            PointF position = _positionAnimation.Value;
            _path.Offset(position.X, position.Y);
            _path.Close();
        }
    }
}