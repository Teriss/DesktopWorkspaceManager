namespace WorkspaceManager.Core;

public sealed record PreviewGeometry(PixelRect HostBounds, PixelRect SourceBounds)
{
    public static PreviewGeometry? Calculate(PreviewPlacement placement, int sourceWidth, int sourceHeight)
    {
        var card = placement.ScreenBounds;
        if (!card.IsValid || !placement.ClipBounds.IsValid || sourceWidth <= 0 || sourceHeight <= 0) return null;
        double scale = Math.Min((double)card.Width / sourceWidth, (double)card.Height / sourceHeight);
        int width = Math.Max(1, (int)Math.Floor(sourceWidth * scale));
        int height = Math.Max(1, (int)Math.Floor(sourceHeight * scale));
        var image = new PixelRect(card.X + (card.Width - width) / 2, card.Y + (card.Height - height) / 2, width, height);
        var clip = placement.ClipBounds;
        int left = Math.Max(image.X, clip.X), top = Math.Max(image.Y, clip.Y);
        int right = Math.Min(image.Right, clip.Right), bottom = Math.Min(image.Bottom, clip.Bottom);
        if (left >= right || top >= bottom) return null;
        // Crop the source as well as the destination. A window region alone does not
        // reliably clip the compositor's DWM thumbnail visual.
        int sourceLeft = Math.Clamp((int)Math.Floor((double)(left - image.X) * sourceWidth / width), 0, sourceWidth - 1);
        int sourceTop = Math.Clamp((int)Math.Floor((double)(top - image.Y) * sourceHeight / height), 0, sourceHeight - 1);
        int sourceRight = Math.Clamp((int)Math.Ceiling((double)(right - image.X) * sourceWidth / width), sourceLeft + 1, sourceWidth);
        int sourceBottom = Math.Clamp((int)Math.Ceiling((double)(bottom - image.Y) * sourceHeight / height), sourceTop + 1, sourceHeight);
        return new(new(left, top, right - left, bottom - top), new(sourceLeft, sourceTop, sourceRight - sourceLeft, sourceBottom - sourceTop));
    }
}
