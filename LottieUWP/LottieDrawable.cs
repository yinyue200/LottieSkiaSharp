﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.UI;
using Windows.UI.Xaml.Media.Imaging;
using MathNet.Numerics.LinearAlgebra.Single;

namespace LottieUWP
{
    /// <summary>
    /// This can be used to show an lottie animation in any place that would normally take a drawable.
    /// If there are masks or mattes, then you MUST call <seealso cref="#recycleBitmaps()"/> when you are done
    /// or else you will leak bitmaps.
    /// <para>
    /// It is preferable to use <seealso cref="com.airbnb.lottie.LottieAnimationView"/> when possible because it
    /// handles bitmap recycling and asynchronous loading
    /// of compositions.
    /// </para>
    /// </summary>
    public class LottieDrawable
    {
        private static readonly string Tag = typeof(LottieDrawable).Name;
        private DenseMatrix _matrix = DenseMatrix.CreateIdentity(3);
        private LottieComposition _composition;
        private readonly ValueAnimator _animator = ValueAnimator.OfFloat(0f, 1f);
        private float _speed = 1f;
        private float _scale = 1f;
        private float _progress;

        private readonly ISet<ColorFilterData> _colorFilterData = new HashSet<ColorFilterData>();
        private ImageAssetBitmapManager _imageAssetBitmapManager;
        private string _imageAssetsFolder;
        private IImageAssetDelegate _imageAssetDelegate;
        private bool _playAnimationWhenCompositionAdded;
        private bool _reverseAnimationWhenCompositionAdded;
        private bool _systemAnimationsAreDisabled;
        private bool _enableMergePaths;
        private CompositionLayer _compositionLayer;
        private int _alpha = 255;

        public LottieDrawable(LottieAnimationView lottieAnimationView)
        {
            _animator.Loop = false;
            _animator.Interpolator = new LinearInterpolator();
            _animator.AddUpdateListener(new AnimatorUpdateListenerAnonymousInnerClass(this, lottieAnimationView));
        }

        public interface IValueAnimatorAnimatorUpdateListener
        {
            void OnAnimationUpdate(ValueAnimator animation);
        }

        private class AnimatorUpdateListenerAnonymousInnerClass : IValueAnimatorAnimatorUpdateListener
        {
            private readonly LottieDrawable _outerInstance;
            private readonly LottieAnimationView _lottieAnimationView;

            public AnimatorUpdateListenerAnonymousInnerClass(LottieDrawable outerInstance, LottieAnimationView lottieAnimationView)
            {
                _outerInstance = outerInstance;
                _lottieAnimationView = lottieAnimationView;
            }

            public void OnAnimationUpdate(ValueAnimator animation)
            {
                if (_outerInstance._systemAnimationsAreDisabled)
                {
                    _outerInstance._animator.Cancel();
                    _outerInstance.Progress = 1f;
                }
                else
                {
                    _outerInstance.Progress = animation.AnimatedValue;
                    if (_lottieAnimationView.Canvas != null)
                    {
                        _lottieAnimationView.Canvas.Bitmap.Clear(Colors.Transparent);
                        _outerInstance.Draw(_lottieAnimationView.Canvas);
                    }
                }
            }
        }

        /// <summary>
        /// Returns whether or not any layers in this composition has masks.
        /// </summary>
        public virtual bool HasMasks()
        {
            return _compositionLayer != null && _compositionLayer.HasMasks();
        }

        /// <summary>
        /// Returns whether or not any layers in this composition has a matte layer.
        /// </summary>
        public virtual bool HasMatte()
        {
            return _compositionLayer != null && _compositionLayer.HasMatte();
        }

        internal virtual bool EnableMergePathsForKitKatAndAbove()
        {
            return _enableMergePaths;
        }

        /// <summary>
        /// Enable this to get merge path support for devices running KitKat (19) and above.
        /// 
        /// Merge paths currently don't work if the the operand shape is entirely contained within the
        /// first shape. If you need to cut out one shape from another shape, use an even-odd fill type
        /// instead of using merge paths.
        /// </summary>
        public virtual void EnableMergePathsForKitKatAndAbove(bool enable)
        {
            _enableMergePaths = enable;
            if (_composition != null)
            {
                BuildCompositionLayer();
            }
        }

        /// <summary>
        /// If you use image assets, you must explicitly specify the folder in assets/ in which they are
        /// located because bodymovin uses the name filenames across all compositions (img_#).
        /// Do NOT rename the images themselves.
        /// 
        /// If your images are located in src/main/assets/airbnb_loader/ then call
        /// `setImageAssetsFolder("airbnb_loader/");`.
        /// 
        /// 
        /// If you use LottieDrawable directly, you MUST call <seealso cref="#recycleBitmaps()"/> when you
        /// are done. Calling <seealso cref="#recycleBitmaps()"/> doesn't have to be final and <seealso cref="LottieDrawable"/>
        /// will recreate the bitmaps if needed but they will leak if you don't recycle them.
        /// </summary>
        public virtual string ImagesAssetsFolder
        {
            set => _imageAssetsFolder = value;
        }

        public virtual string ImageAssetsFolder => _imageAssetsFolder;

        /// <summary>
        /// If you have image assets and use <seealso cref="LottieDrawable"/> directly, you must call this yourself.
        /// 
        /// Calling recycleBitmaps() doesn't have to be final and <seealso cref="LottieDrawable"/>
        /// will recreate the bitmaps if needed but they will leak if you don't recycle them.
        /// 
        /// </summary>
        public virtual void RecycleBitmaps()
        {
            _imageAssetBitmapManager?.RecycleBitmaps();
        }

        /// <returns> True if the composition is different from the previously set composition, false otherwise. </returns>
        public virtual bool SetComposition(LottieComposition composition)
        {
            //if (Callback == null) // TODO: needed?
            //{
            //    throw new System.InvalidOperationException("You or your view must set a Drawable.Callback before setting the composition. This " + "gets done automatically when added to an ImageView. " + "Either call ImageView.setImageDrawable() before setComposition() or call " + "setCallback(yourView.getCallback()) first.");
            //}

            if (_composition == composition)
            {
                return false;
            }

            ClearComposition();
            _composition = composition;
            Speed = _speed;
            Scale = 1f;
            UpdateBounds();
            BuildCompositionLayer();
            ApplyColorFilters();

            Progress = _progress;
            if (_playAnimationWhenCompositionAdded)
            {
                _playAnimationWhenCompositionAdded = false;
                PlayAnimation();
            }
            if (_reverseAnimationWhenCompositionAdded)
            {
                _reverseAnimationWhenCompositionAdded = false;
                ReverseAnimation();
            }

            return true;
        }

        private void BuildCompositionLayer()
        {
            _compositionLayer = new CompositionLayer(this, Layer.Factory.NewInstance(_composition), _composition.Layers, _composition);
        }

        private void ApplyColorFilters()
        {
            if (_compositionLayer == null)
            {
                return;
            }

            foreach (ColorFilterData data in _colorFilterData)
            {
                _compositionLayer.AddColorFilter(data.LayerName, data.ContentName, data._colorFilter);
            }
        }

        private void ClearComposition()
        {
            RecycleBitmaps();
            _compositionLayer = null;
            _imageAssetBitmapManager = null;
            InvalidateSelf();
        }

        public void InvalidateSelf()
        {
            //InvalidateArrange(); // TODO: is this the equivalent?
            //InvalidateMeasure();

            //Callback callback = Callback;
            //if (callback != null)
            //{
            //    callback.invalidateDrawable(this);
            //}
        }

        public void SetAlpha(int alpha)
        {
            _alpha = alpha;
        }

        public int GetAlpha()
        {
            return _alpha;
        }

        public ColorFilter ColorFilter
        {
            set
            {
                // Do nothing.
            }
        }

        /// <summary>
        /// Add a color filter to specific content on a specific layer. </summary>
        /// <param name="layerName"> name of the layer where the supplied content name lives </param>
        /// <param name="contentName"> name of the specific content that the color filter is to be applied </param>
        /// <param name="colorFilter"> the color filter, null to clear the color filter </param>
        public virtual void AddColorFilterToContent(string layerName, string contentName, ColorFilter colorFilter)
        {
            AddColorFilterInternal(layerName, contentName, colorFilter);
        }

        /// <summary>
        /// Add a color filter to a whole layer </summary>
        /// <param name="layerName"> name of the layer that the color filter is to be applied </param>
        /// <param name="colorFilter"> the color filter, null to clear the color filter </param>
        public virtual void AddColorFilterToLayer(string layerName, ColorFilter colorFilter)
        {
            AddColorFilterInternal(layerName, null, colorFilter);
        }

        /// <summary>
        /// Add a color filter to all layers </summary>
        /// <param name="colorFilter"> the color filter, null to clear all color filters </param>
        public virtual void AddColorFilter(ColorFilter colorFilter)
        {
            AddColorFilterInternal(null, null, colorFilter);
        }

        /// <summary>
        /// Clear all color filters on all layers and all content in the layers
        /// </summary>
        public virtual void ClearColorFilters()
        {
            _colorFilterData.Clear();
            AddColorFilterInternal(null, null, null);
        }

        /// <summary>
        /// Private method to capture all color filter additions.
        /// There are 3 different behaviors here.
        /// 1. layerName is null. All layers supporting color filters will apply the passed in color filter
        /// 2. layerName is not null, contentName is null. This will apply the passed in color filter
        ///    to the whole layer
        /// 3. layerName is not null, contentName is not null. This will apply the pass in color filter
        ///    to a specific composition content.
        /// </summary>
        private void AddColorFilterInternal(string layerName, string contentName, ColorFilter colorFilter)
        {
            ColorFilterData data = new ColorFilterData(layerName, contentName, colorFilter);
            if (colorFilter == null && _colorFilterData.Contains(data))
            {
                _colorFilterData.Remove(data);
            }
            else
            {
                _colorFilterData.Add(new ColorFilterData(layerName, contentName, colorFilter));
            }

            _compositionLayer?.AddColorFilter(layerName, contentName, colorFilter);
        }

        //public int Opacity
        //{
        //    get
        //    {
        //        return PixelFormat.TRANSLUCENT;
        //    }
        //}

        public void Draw(BitmapCanvas canvas)
        {
            if (_compositionLayer == null)
            {
                return;
            }
            float scale = _scale;
            if (_compositionLayer.HasMatte())
            {
                scale = Math.Min(_scale, GetMaxScale(canvas));
            }

            _matrix.Reset();
            _matrix = MatrixExt.PreScale(_matrix, scale, scale);
            _compositionLayer.Draw(canvas, _matrix, _alpha);
        }

        internal virtual void SystemAnimationsAreDisabled()
        {
            _systemAnimationsAreDisabled = true;
        }

        public virtual bool Looping
        {
            get => _animator.Loop;
            set => _animator.Loop = value;
        }

        public virtual bool Animating => _animator.Running;

        public virtual void PlayAnimation()
        {
            PlayAnimation(_progress > 0.0 && _progress < 1.0);
        }

        public virtual void ResumeAnimation()
        {
            PlayAnimation(true);
        }

        private void PlayAnimation(bool setStartTime)
        {
            if (_compositionLayer == null)
            {
                _playAnimationWhenCompositionAdded = true;
                _reverseAnimationWhenCompositionAdded = false;
                return;
            }
            long playTime = setStartTime ? (long)(_progress * _animator.Duration) : 0;
            _animator.Start();
            if (setStartTime)
            {
                _animator.CurrentPlayTime = playTime;
            }
        }

        public virtual void ResumeReverseAnimation()
        {
            ReverseAnimation(true);
        }

        public virtual void ReverseAnimation()
        {
            ReverseAnimation(_progress > 0.0 && _progress < 1.0);
        }

        private void ReverseAnimation(bool setStartTime)
        {
            if (_compositionLayer == null)
            {
                _playAnimationWhenCompositionAdded = false;
                _reverseAnimationWhenCompositionAdded = true;
                return;
            }
            if (setStartTime)
            {
                _animator.CurrentPlayTime = (long)(_progress * _animator.Duration);
            }
            _animator.Reverse();
        }

        public virtual float Speed
        {
            set
            {
                _speed = value;
                if (value < 0)
                {
                    _animator.SetFloatValues(1f, 0f);
                }
                else
                {
                    _animator.SetFloatValues(0f, 1f);
                }

                if (_composition != null)
                {
                    _animator.Duration = (long)(_composition.Duration / Math.Abs(value));
                }
            }
        }

        public virtual float Progress
        {
            set
            {
                _progress = value;
                if (_compositionLayer != null)
                {
                    _compositionLayer.Progress = value;
                }
            }
            get => _progress;
        }


        /// <summary>
        /// Set the scale on the current composition. The only cost of this function is re-rendering the
        /// current frame so you may call it frequent to scale something up or down.
        /// 
        /// The smaller the animation is, the better the performance will be. You may find that scaling an
        /// animation down then rendering it in a larger ImageView and letting ImageView scale it back up
        /// with a scaleType such as centerInside will yield better performance with little perceivable
        /// quality loss.
        /// </summary>
        public virtual float Scale
        {
            set
            {
                _scale = value;
                UpdateBounds();
            }
            get => _scale;
        }

        /// <summary>
        /// Use this if you can't bundle images with your app. This may be useful if you download the
        /// animations from the network or have the images saved to an SD Card. In that case, Lottie
        /// will defer the loading of the bitmap to this delegate.
        /// </summary>
        public virtual IImageAssetDelegate ImageAssetDelegate
        {
            set
            {
                _imageAssetDelegate = value;
                if (_imageAssetBitmapManager != null)
                {
                    _imageAssetBitmapManager.AssetDelegate = value;
                }
            }
        }

        public virtual LottieComposition Composition => _composition;

        private void UpdateBounds()
        {
            if (_composition == null)
            {
                return;
            }
            Width = (int)(_composition.Bounds.Width * _scale);
            Height = (int)(_composition.Bounds.Height * _scale);
        }

        public int Width { get; set; }
        public int Height { get; set; }

        public virtual void CancelAnimation()
        {
            _playAnimationWhenCompositionAdded = false;
            _reverseAnimationWhenCompositionAdded = false;
            _animator.Cancel();
        }

        public virtual void AddAnimatorUpdateListener(IValueAnimatorAnimatorUpdateListener updateListener)
        {
            _animator.AddUpdateListener(updateListener);
        }

        public virtual void RemoveAnimatorUpdateListener(IValueAnimatorAnimatorUpdateListener updateListener)
        {
            _animator.RemoveUpdateListener(updateListener);
        }

        public virtual void AddAnimatorListener(Animator.IAnimatorListener listener)
        {
            _animator.AddListener(listener);
        }

        public virtual void RemoveAnimatorListener(Animator.IAnimatorListener listener)
        {
            _animator.RemoveListener(listener);
        }

        public int IntrinsicWidth => _composition == null ? -1 : (int)(_composition.Bounds.Width * _scale);

        public int IntrinsicHeight => _composition == null ? -1 : (int)(_composition.Bounds.Height * _scale);

        /// 
        /// <summary>
        /// Allows you to modify or clear a bitmap that was loaded for an image either automatically
        /// 
        /// through <seealso cref="#setImagesAssetsFolder(String)"/> or with an <seealso cref="ImageAssetDelegate"/>.
        /// 
        /// 
        /// </summary>
        /// <returns> the previous Bitmap or null.
        ///  </returns>

        public virtual WriteableBitmap UpdateBitmap(string id, WriteableBitmap bitmap)
        {
            ImageAssetBitmapManager bm = ImageAssetBitmapManager;
            if (bm == null)
            {
                Debug.WriteLine("Cannot update bitmap. Most likely the drawable is not added to a View " + "which prevents Lottie from getting a Context.", L.Tag);
                return null;
            }
            WriteableBitmap ret = bm.UpdateBitmap(id, bitmap);
            InvalidateSelf();
            return ret;
        }

        internal virtual WriteableBitmap GetImageAsset(string id)
        {
            return ImageAssetBitmapManager?.BitmapForId(id);
        }

        private ImageAssetBitmapManager ImageAssetBitmapManager
        {
            get
            {
                if (_imageAssetBitmapManager != null && false)//!imageAssetBitmapManager.hasSameContext(Context))
                {
                    _imageAssetBitmapManager.RecycleBitmaps();
                    _imageAssetBitmapManager = null;
                }

                if (_imageAssetBitmapManager == null)
                {
                    _imageAssetBitmapManager = new ImageAssetBitmapManager(_imageAssetsFolder, _imageAssetDelegate, _composition.Images);
                }

                return _imageAssetBitmapManager;
            }
        }

        private float GetMaxScale(BitmapCanvas canvas)
        {
            float maxScaleX = (float)canvas.Width / (float)_composition.Bounds.Width;
            float maxScaleY = (float)canvas.Height / (float)_composition.Bounds.Height;
            return Math.Min(maxScaleX, maxScaleY);
        }

        ///// <summary>
        ///// These Drawable.Callback methods proxy the calls so that this is the drawable that is
        ///// actually invalidated, not a child one which will not pass the view's validateDrawable check.
        ///// </summary>
        //public void invalidateDrawable(Drawable who)
        //{
        //    Callback callback = Callback;
        //    if (callback == null)
        //    {
        //        return;
        //    }
        //    callback.invalidateDrawable(this);
        //}

        //public void scheduleDrawable(Drawable who, ThreadStart what, long when)
        //{
        //    Callback callback = Callback;
        //    if (callback == null)
        //    {
        //        return;
        //    }
        //    callback.scheduleDrawable(this, what, when);
        //}

        //public void unscheduleDrawable(Drawable who, ThreadStart what)
        //{
        //    Callback callback = Callback;
        //    if (callback == null)
        //    {
        //        return;
        //    }
        //    callback.unscheduleDrawable(this, what);
        //}

        private class ColorFilterData
        {
            internal readonly string LayerName;
            internal readonly string ContentName;
            internal readonly ColorFilter _colorFilter;

            internal ColorFilterData(string layerName, string contentName, ColorFilter colorFilter)
            {
                LayerName = layerName;
                ContentName = contentName;
                _colorFilter = colorFilter;
            }

            public override int GetHashCode()
            {
                int hashCode = 17;
                if (!string.IsNullOrEmpty(LayerName))
                {
                    hashCode = hashCode * 31 * LayerName.GetHashCode();
                }

                if (!string.IsNullOrEmpty(ContentName))
                {
                    hashCode = hashCode * 31 * ContentName.GetHashCode();
                }
                return hashCode;
            }

            public override bool Equals(object obj)
            {
                if (this == obj)
                {
                    return true;
                }

                if (!(obj is ColorFilterData))
                {
                    return false;
                }

                ColorFilterData other = (ColorFilterData)obj;

                return GetHashCode() == other.GetHashCode() && _colorFilter == other._colorFilter;
            }
        }
    }
}