using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;

namespace FluentDL.Helpers
{
    class AnimationHelper
    {
        public static Vector3 GetCenterPoint(FrameworkElement fwElement)
        {
            return new Vector3((float)fwElement.ActualWidth / 2, (float)fwElement.ActualHeight / 2, 0.0f);
        }

        public static Vector3KeyFrameAnimation CreateScaleUp(Visual visual, float scale, double durationMs)
        {
            var compositor = visual.Compositor;

            // Create the scale up animation
            var scaleUpAnimation = compositor.CreateVector3KeyFrameAnimation();
            scaleUpAnimation.InsertKeyFrame(0.0f, new Vector3(1.0f, 1.0f, 1.0f));
            scaleUpAnimation.InsertKeyFrame(1.0f, new Vector3(scale, scale, 1.0f));
            scaleUpAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);

            return scaleUpAnimation;
        }

        public static Vector3KeyFrameAnimation CreateScaleDown(Visual visual, float scale, double durationMs)
        {
            var compositor = visual.Compositor;

            // Create the scale down animation
            var scaleDownAnimation = compositor.CreateVector3KeyFrameAnimation();
            scaleDownAnimation.InsertKeyFrame(0.0f, new Vector3(scale, scale, 1.0f));
            scaleDownAnimation.InsertKeyFrame(1.0f, new Vector3(1.0f, 1.0f, 1.0f));
            scaleDownAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);

            return scaleDownAnimation;
        }

        public static Vector3KeyFrameAnimation CreateSpringDown(Visual visual, float pixelsDown, double durationMs)
        {
            var compositor = visual.Compositor;

            // Create the spring down animation
            var springDownAnimation = compositor.CreateVector3KeyFrameAnimation();
            springDownAnimation.InsertKeyFrame(0.0f, new Vector3(0.0f, -pixelsDown, 0.0f));
            springDownAnimation.InsertKeyFrame(0.5f, new Vector3(0.0f, pixelsDown, 0.0f));
            springDownAnimation.InsertKeyFrame(1.0f, new Vector3(0.0f, 0.0f, 0.0f));
            springDownAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);

            return springDownAnimation;
        }

        public static void AttachScaleAnimation(AppBarButton button)
        {
            var visual = ElementCompositionPreview.GetElementVisual(button.Icon);
            var scaleUpAnimation = CreateScaleUp(visual, 1.3f, 200);
            var scaleDownAnimation = CreateScaleDown(visual, 1.3f, 200);
            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, e) =>
            {
                // Set the center point for scaling
                visual.CenterPoint = GetCenterPoint(button.Icon);
                visual.StartAnimation("Scale", scaleUpAnimation);
            }), true);
            button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((s, e) =>
            {
                // Set the center point for scaling
                visual.CenterPoint = GetCenterPoint(button.Icon);
                visual.StartAnimation("Scale", scaleDownAnimation);
            }), true);
        }
    }
}