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

        public static Vector3KeyFrameAnimation CreateSpringUp(Visual visual, float pixelsUp, double durationMs)
        {
            var compositor = visual.Compositor;

            // Create the spring up animation
            var springUpAnimation = compositor.CreateVector3KeyFrameAnimation();
            springUpAnimation.InsertKeyFrame(0.0f, new Vector3(0.0f, pixelsUp, 0.0f));
            springUpAnimation.InsertKeyFrame(0.5f, new Vector3(0.0f, -pixelsUp, 0.0f));
            springUpAnimation.InsertKeyFrame(1.0f, new Vector3(0.0f, 0.0f, 0.0f));
            springUpAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);

            return springUpAnimation;
        }

        // Offset animation for the x axis, positive is to the right, negative is to the left
        public static Vector3KeyFrameAnimation CreateSpringX(Visual visual, float offsetX, double durationMs)
        {
            var compositor = visual.Compositor;

            // Create the spring x animation
            var springXAnimation = compositor.CreateVector3KeyFrameAnimation();
            springXAnimation.InsertKeyFrame(0.0f, new Vector3(-offsetX, 0.0f, 0.0f));
            springXAnimation.InsertKeyFrame(0.5f, new Vector3(offsetX, 0.0f, 0.0f));
            springXAnimation.InsertKeyFrame(1.0f, new Vector3(0.0f, 0.0f, 0.0f));

            return springXAnimation;
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

        public static void AttachScaleAnimation(Button button, FrameworkElement icon)
        {
            var visual = ElementCompositionPreview.GetElementVisual(icon);
            var scaleUpAnimation = AnimationHelper.CreateScaleUp(visual, 1.3f, 200);
            var scaleDownAnimation = AnimationHelper.CreateScaleDown(visual, 1.3f, 200);

            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, e) =>
            {
                // Set the center point for scaling
                visual.CenterPoint = AnimationHelper.GetCenterPoint(icon);
                visual.StartAnimation("Scale", scaleUpAnimation);
            }), true);

            button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((s, e) =>
            {
                // Set the center point for scaling
                visual.CenterPoint = AnimationHelper.GetCenterPoint(icon);
                visual.StartAnimation("Scale", scaleDownAnimation);
            }), true);
        }

        public static void AttachSpringDownAnimation(AppBarButton button)
        {
            var visual = ElementCompositionPreview.GetElementVisual(button.Icon);
            var springDownAnimation = CreateSpringDown(visual, 4, 500);
            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, e) =>
            {
                visual.StartAnimation("Offset", springDownAnimation);
            }), true);
        }

        public static void AttachSpringUpAnimation(AppBarButton button)
        {
            var visual = ElementCompositionPreview.GetElementVisual(button.Icon);
            var springUpAnimation = CreateSpringUp(visual, 4, 500);
            button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((s, e) =>
            {
                visual.StartAnimation("Offset", springUpAnimation);
            }), true);
        }

        public static void AttachSpringDownAnimation(Button button, FrameworkElement icon)
        {
            var visual = ElementCompositionPreview.GetElementVisual(icon);
            var springDownAnimation = CreateSpringDown(visual, 4, 500);
            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, e) =>
            {
                visual.StartAnimation("Offset", springDownAnimation);
            }), true);
        }

        public static void AttachSpringUpAnimation(Button button, FrameworkElement icon)
        {
            var visual = ElementCompositionPreview.GetElementVisual(icon);
            var springUpAnimation = CreateSpringUp(visual, 4, 500);
            button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((s, e) =>
            {
                visual.StartAnimation("Offset", springUpAnimation);
            }), true);
        }

        public static void AttachSpringXAnimation(AppBarButton button, float offsetX)
        {
            var visual = ElementCompositionPreview.GetElementVisual(button.Icon);
            var springXAnimation = CreateSpringX(visual, offsetX, 500);
            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, e) =>
            {
                visual.StartAnimation("Offset", springXAnimation);
            }), true);
        }

        public static void AttachSpringXAnimation(Button button, FrameworkElement icon, float offsetX)
        {
            var visual = ElementCompositionPreview.GetElementVisual(icon);
            var springXAnimation = CreateSpringX(visual, offsetX, 500);
            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, e) =>
            {
                visual.StartAnimation("Offset", springXAnimation);
            }), true);
        }


        public static void AttachSpringRightAnimation(AppBarButton button)
        {
            AttachSpringXAnimation(button, 4);
        }

        public static void AttachSpringRightAnimation(Button button, FrameworkElement icon)
        {
            AttachSpringXAnimation(button, icon, 4);
        }

        public static void AttachSpringLeftAnimation(AppBarButton button)
        {
            AttachSpringXAnimation(button, -4);
        }

        public static void AttachSpringLeftAnimation(Button button, FrameworkElement icon)
        {
            AttachSpringXAnimation(button, icon, -4);
        }
    }
}