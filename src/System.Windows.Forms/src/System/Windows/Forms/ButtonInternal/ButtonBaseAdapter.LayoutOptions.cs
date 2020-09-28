﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms.ButtonInternal
{
    internal abstract partial class ButtonBaseAdapter
    {
        internal class LayoutOptions
        {
            private bool _disableWordWrapping;

            // If this is changed to a property callers will need to be updated
            // as they modify fields in the Rectangle.
            public Rectangle Client;

            public bool GrowBorderBy1PxWhenDefault { get; set; }
            public bool IsDefault { get; set; }
            public int BorderSize { get; set; }
            public int PaddingSize { get; set; }
            public bool MaxFocus { get; set; }
            public bool FocusOddEvenFixup { get; set; }
            public Font Font { get; set; }
            public string Text { get; set; }
            public Size ImageSize { get; set; }
            public int CheckSize { get; set; }
            public int CheckPaddingSize { get; set; }
            public ContentAlignment CheckAlign { get; set; }
            public ContentAlignment ImageAlign { get; set; }
            public ContentAlignment TextAlign { get; set; }
            public TextImageRelation TextImageRelation { get; set; }
            public bool HintTextUp { get; set; }
            public bool TextOffset { get; set; }
            public bool ShadowedText { get; set; }
            public bool LayoutRTL { get; set; }
            public bool VerticalText { get; set; }
            public bool UseCompatibleTextRendering { get; set; }
            public bool EverettButtonCompat { get; set; } = true;
            public TextFormatFlags GdiTextFormatFlags { get; set; } = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
            public StringFormatFlags GdiPlusFormatFlags { get; set; }
            public StringTrimming GdiPlusTrimming { get; set; }
            public HotkeyPrefix GdiPlusHotkeyPrefix { get; set; }
            public StringAlignment GdiPlusAlignment { get; set; } // horizontal alignment.
            public StringAlignment GdiPlusLineAlignment { get; set; } // vertical alignment.

            /// <summary>
            ///  We don't cache the StringFormat itself because we don't have a deterministic way of disposing it, instead
            ///  we cache the flags that make it up and create it on demand so it can be disposed by calling code.
            /// </summary>
            public StringFormat StringFormat
            {
                get
                {
                    StringFormat format = new StringFormat
                    {
                        FormatFlags = GdiPlusFormatFlags,
                        Trimming = GdiPlusTrimming,
                        HotkeyPrefix = GdiPlusHotkeyPrefix,
                        Alignment = GdiPlusAlignment,
                        LineAlignment = GdiPlusLineAlignment
                    };

                    if (_disableWordWrapping)
                    {
                        format.FormatFlags |= StringFormatFlags.NoWrap;
                    }

                    return format;
                }
                set
                {
                    GdiPlusFormatFlags = value.FormatFlags;
                    GdiPlusTrimming = value.Trimming;
                    GdiPlusHotkeyPrefix = value.HotkeyPrefix;
                    GdiPlusAlignment = value.Alignment;
                    GdiPlusLineAlignment = value.LineAlignment;
                }
            }

            /// <summary>
            /// </summary>
            public TextFormatFlags TextFormatFlags
            {
                get
                {
                    if (_disableWordWrapping)
                    {
                        return GdiTextFormatFlags & ~TextFormatFlags.WordBreak;
                    }

                    return GdiTextFormatFlags;
                }
                //set {
                //    this.gdiTextFormatFlags = value;
                //}
            }

            // textImageInset compensates for two factors: 3d text when the button is disabled,
            // and moving text on 3d-look buttons. These factors make the text require a couple
            // more pixels of space.  We inset image by the same amount so they line up.
            public int TextImageInset { get; set; } = 2;

            public Padding Padding { get; set; }

            private static readonly int s_combineCheck = BitVector32.CreateMask();
            private static readonly int s_combineImageText = BitVector32.CreateMask(s_combineCheck);

            private enum Composition
            {
                NoneCombined = 0x00,
                CheckCombined = 0x01,
                TextImageCombined = 0x02,
                AllCombined = 0x03
            }

            // Uses checkAlign, imageAlign, and textAlign to figure out how to compose
            // checkSize, imageSize, and textSize into the preferredSize.
            private Size Compose(Size checkSize, Size imageSize, Size textSize)
            {
                Composition hComposition = GetHorizontalComposition();
                Composition vComposition = GetVerticalComposition();
                return new Size(
                    xCompose(hComposition, checkSize.Width, imageSize.Width, textSize.Width),
                    xCompose(vComposition, checkSize.Height, imageSize.Height, textSize.Height)
                );
            }

            private int xCompose(Composition composition, int checkSize, int imageSize, int textSize)
            {
                switch (composition)
                {
                    case Composition.NoneCombined:
                        return checkSize + imageSize + textSize;
                    case Composition.CheckCombined:
                        return Math.Max(checkSize, imageSize + textSize);
                    case Composition.TextImageCombined:
                        return Math.Max(imageSize, textSize) + checkSize;
                    case Composition.AllCombined:
                        return Math.Max(Math.Max(checkSize, imageSize), textSize);
                    default:
                        Debug.Fail(string.Format(SR.InvalidArgument, nameof(composition), composition.ToString()));
                        return -7107;
                }
            }

            // Uses checkAlign, imageAlign, and textAlign to figure out how to decompose
            // proposedSize into just the space left over for text.
            private Size Decompose(Size checkSize, Size imageSize, Size proposedSize)
            {
                Composition hComposition = GetHorizontalComposition();
                Composition vComposition = GetVerticalComposition();
                return new Size(
                    xDecompose(hComposition, checkSize.Width, imageSize.Width, proposedSize.Width),
                    xDecompose(vComposition, checkSize.Height, imageSize.Height, proposedSize.Height)
                );
            }

            private int xDecompose(Composition composition, int checkSize, int imageSize, int proposedSize)
            {
                switch (composition)
                {
                    case Composition.NoneCombined:
                        return proposedSize - (checkSize + imageSize);
                    case Composition.CheckCombined:
                        return proposedSize - imageSize;
                    case Composition.TextImageCombined:
                        return proposedSize - checkSize;
                    case Composition.AllCombined:
                        return proposedSize;
                    default:
                        Debug.Fail(string.Format(SR.InvalidArgument, nameof(composition), composition.ToString()));
                        return -7109;
                }
            }

            private Composition GetHorizontalComposition()
            {
                BitVector32 action = new BitVector32();

                // Checks reserve space horizontally if possible, so only AnyLeft/AnyRight prevents combination.
                action[s_combineCheck] = CheckAlign == ContentAlignment.MiddleCenter || !LayoutUtils.IsHorizontalAlignment(CheckAlign);
                action[s_combineImageText] = !LayoutUtils.IsHorizontalRelation(TextImageRelation);
                return (Composition)action.Data;
            }

            internal Size GetPreferredSizeCore(Size proposedSize)
            {
                // Get space required for border and padding
                //
                int linearBorderAndPadding = BorderSize * 2 + PaddingSize * 2;
                if (GrowBorderBy1PxWhenDefault)
                {
                    linearBorderAndPadding += 2;
                }
                Size bordersAndPadding = new Size(linearBorderAndPadding, linearBorderAndPadding);
                proposedSize -= bordersAndPadding;

                // Get space required for Check
                //
                int checkSizeLinear = FullCheckSize;
                Size checkSize = checkSizeLinear > 0 ? new Size(checkSizeLinear + 1, checkSizeLinear) : Size.Empty;

                // Get space required for Image - textImageInset compensated for by expanding image.
                //
                Size textImageInsetSize = new Size(TextImageInset * 2, TextImageInset * 2);
                Size requiredImageSize = (ImageSize != Size.Empty) ? ImageSize + textImageInsetSize : Size.Empty;

                // Pack Text into remaning space
                //
                proposedSize -= textImageInsetSize;
                proposedSize = Decompose(checkSize, requiredImageSize, proposedSize);

                Size textSize = Size.Empty;

                if (!string.IsNullOrEmpty(Text))
                {
                    // When Button.AutoSizeMode is set to GrowOnly TableLayoutPanel expects buttons not to automatically wrap on word break. If
                    // there's enough room for the text to word-wrap then it will happen but the layout would not be adjusted to allow text wrapping.
                    // If someone has a carriage return in the text we'll honor that for preferred size, but we wont wrap based on constraints.
                    try
                    {
                        _disableWordWrapping = true;
                        textSize = GetTextSize(proposedSize) + textImageInsetSize;
                    }
                    finally
                    {
                        _disableWordWrapping = false;
                    }
                }

                // Combine pieces to get final preferred size
                //
                Size requiredSize = Compose(checkSize, ImageSize, textSize);
                requiredSize += bordersAndPadding;

                return requiredSize;
            }

            private Composition GetVerticalComposition()
            {
                BitVector32 action = new BitVector32();

                // Checks reserve space horizontally if possible, so only Top/Bottom prevents combination.
                action[s_combineCheck] = CheckAlign == ContentAlignment.MiddleCenter || !LayoutUtils.IsVerticalAlignment(CheckAlign);
                action[s_combineImageText] = !LayoutUtils.IsVerticalRelation(TextImageRelation);
                return (Composition)action.Data;
            }

            private int FullBorderSize
            {
                get
                {
                    int result = BorderSize;
                    if (OnePixExtraBorder)
                    {
                        BorderSize++;
                    }
                    return BorderSize;
                }
            }

            private bool OnePixExtraBorder
            {
                get { return GrowBorderBy1PxWhenDefault && IsDefault; }
            }

            internal LayoutData Layout()
            {
                LayoutData layout = new LayoutData(this)
                {
                    client = Client
                };

                // subtract border size from layout area
                int fullBorderSize = FullBorderSize;
                layout.face = Rectangle.Inflate(layout.client, -fullBorderSize, -fullBorderSize);

                // checkBounds, checkArea, field
                //
                CalcCheckmarkRectangle(layout);

                // imageBounds, imageLocation, textBounds
                LayoutTextAndImage(layout);

                // focus
                //
                if (MaxFocus)
                {
                    layout.focus = layout.field;
                    layout.focus.Inflate(-1, -1);

                    // Adjust for padding.
                    layout.focus = LayoutUtils.InflateRect(layout.focus, Padding);
                }
                else
                {
                    Rectangle textAdjusted = new Rectangle(layout.textBounds.X - 1, layout.textBounds.Y - 1,
                                                           layout.textBounds.Width + 2, layout.textBounds.Height + 3);
                    if (ImageSize != Size.Empty)
                    {
                        layout.focus = Rectangle.Union(textAdjusted, layout.imageBounds);
                    }
                    else
                    {
                        layout.focus = textAdjusted;
                    }
                }
                if (FocusOddEvenFixup)
                {
                    if (layout.focus.Height % 2 == 0)
                    {
                        layout.focus.Y++;
                        layout.focus.Height--;
                    }
                    if (layout.focus.Width % 2 == 0)
                    {
                        layout.focus.X++;
                        layout.focus.Width--;
                    }
                }

                return layout;
            }

            TextImageRelation RtlTranslateRelation(TextImageRelation relation)
            {
                // If RTL, we swap ImageBeforeText and TextBeforeImage
                if (LayoutRTL)
                {
                    switch (relation)
                    {
                        case TextImageRelation.ImageBeforeText:
                            return TextImageRelation.TextBeforeImage;
                        case TextImageRelation.TextBeforeImage:
                            return TextImageRelation.ImageBeforeText;
                    }
                }
                return relation;
            }

            internal ContentAlignment RtlTranslateContent(ContentAlignment align)
            {
                if (LayoutRTL)
                {
                    ContentAlignment[][] mapping = new ContentAlignment[3][];
                    mapping[0] = new ContentAlignment[2] { ContentAlignment.TopLeft, ContentAlignment.TopRight };
                    mapping[1] = new ContentAlignment[2] { ContentAlignment.MiddleLeft, ContentAlignment.MiddleRight };
                    mapping[2] = new ContentAlignment[2] { ContentAlignment.BottomLeft, ContentAlignment.BottomRight };

                    for (int i = 0; i < 3; ++i)
                    {
                        if (mapping[i][0] == align)
                        {
                            return mapping[i][1];
                        }
                        else if (mapping[i][1] == align)
                        {
                            return mapping[i][0];
                        }
                    }
                }
                return align;
            }

            private int FullCheckSize
            {
                get
                {
                    return CheckSize + CheckPaddingSize;
                }
            }

            void CalcCheckmarkRectangle(LayoutData layout)
            {
                int checkSizeFull = FullCheckSize;
                layout.checkBounds = new Rectangle(Client.X, Client.Y, checkSizeFull, checkSizeFull);

                // Translate checkAlign for Rtl applications
                ContentAlignment align = RtlTranslateContent(CheckAlign);

                Rectangle field = Rectangle.Inflate(layout.face, -PaddingSize, -PaddingSize);

                layout.field = field;

                if (checkSizeFull > 0)
                {
                    if ((align & LayoutUtils.AnyRight) != 0)
                    {
                        layout.checkBounds.X = (field.X + field.Width) - layout.checkBounds.Width;
                    }
                    else if ((align & LayoutUtils.AnyCenter) != 0)
                    {
                        layout.checkBounds.X = field.X + (field.Width - layout.checkBounds.Width) / 2;
                    }

                    if ((align & LayoutUtils.AnyBottom) != 0)
                    {
                        layout.checkBounds.Y = (field.Y + field.Height) - layout.checkBounds.Height;
                    }
                    else if ((align & LayoutUtils.AnyTop) != 0)
                    {
                        layout.checkBounds.Y = field.Y + 2; // + 2: this needs to be aligned to the text (
                    }
                    else
                    {
                        layout.checkBounds.Y = field.Y + (field.Height - layout.checkBounds.Height) / 2;
                    }

                    switch (align)
                    {
                        case ContentAlignment.TopLeft:
                        case ContentAlignment.MiddleLeft:
                        case ContentAlignment.BottomLeft:
                            layout.checkArea.X = field.X;
                            layout.checkArea.Width = checkSizeFull + 1;

                            layout.checkArea.Y = field.Y;
                            layout.checkArea.Height = field.Height;

                            layout.field.X += checkSizeFull + 1;
                            layout.field.Width -= checkSizeFull + 1;
                            break;
                        case ContentAlignment.TopRight:
                        case ContentAlignment.MiddleRight:
                        case ContentAlignment.BottomRight:
                            layout.checkArea.X = field.X + field.Width - checkSizeFull;
                            layout.checkArea.Width = checkSizeFull + 1;

                            layout.checkArea.Y = field.Y;
                            layout.checkArea.Height = field.Height;

                            layout.field.Width -= checkSizeFull + 1;
                            break;
                        case ContentAlignment.TopCenter:
                            layout.checkArea.X = field.X;
                            layout.checkArea.Width = field.Width;

                            layout.checkArea.Y = field.Y;
                            layout.checkArea.Height = checkSizeFull;

                            layout.field.Y += checkSizeFull;
                            layout.field.Height -= checkSizeFull;
                            break;

                        case ContentAlignment.BottomCenter:
                            layout.checkArea.X = field.X;
                            layout.checkArea.Width = field.Width;

                            layout.checkArea.Y = field.Y + field.Height - checkSizeFull;
                            layout.checkArea.Height = checkSizeFull;

                            layout.field.Height -= checkSizeFull;
                            break;

                        case ContentAlignment.MiddleCenter:
                            layout.checkArea = layout.checkBounds;
                            break;
                    }

                    layout.checkBounds.Width -= CheckPaddingSize;
                    layout.checkBounds.Height -= CheckPaddingSize;
                }
            }

            // Maps an image align to the set of TextImageRelations that represent the same edge.
            // For example, imageAlign = TopLeft maps to TextImageRelations ImageAboveText (top)
            // and ImageBeforeText (left).
            private static readonly TextImageRelation[] _imageAlignToRelation = new TextImageRelation[] {
                /* TopLeft = */       TextImageRelation.ImageAboveText | TextImageRelation.ImageBeforeText,
                /* TopCenter = */     TextImageRelation.ImageAboveText,
                /* TopRight = */      TextImageRelation.ImageAboveText | TextImageRelation.TextBeforeImage,
                /* Invalid */         0,
                /* MiddleLeft = */    TextImageRelation.ImageBeforeText,
                /* MiddleCenter = */  0,
                /* MiddleRight = */   TextImageRelation.TextBeforeImage,
                /* Invalid */         0,
                /* BottomLeft = */    TextImageRelation.TextAboveImage | TextImageRelation.ImageBeforeText,
                /* BottomCenter = */  TextImageRelation.TextAboveImage,
                /* BottomRight = */   TextImageRelation.TextAboveImage | TextImageRelation.TextBeforeImage
            };

            private static TextImageRelation ImageAlignToRelation(ContentAlignment alignment)
            {
                return _imageAlignToRelation[LayoutUtils.ContentAlignmentToIndex(alignment)];
            }

            private static TextImageRelation TextAlignToRelation(ContentAlignment alignment)
            {
                return LayoutUtils.GetOppositeTextImageRelation(ImageAlignToRelation(alignment));
            }

            internal void LayoutTextAndImage(LayoutData layout)
            {
                // Translate for Rtl applications.  This intentially shadows the member variables.
                ContentAlignment imageAlign = RtlTranslateContent(this.ImageAlign);
                ContentAlignment textAlign = RtlTranslateContent(this.TextAlign);
                TextImageRelation textImageRelation = RtlTranslateRelation(this.TextImageRelation);

                // Figure out the maximum bounds for text & image
                Rectangle maxBounds = Rectangle.Inflate(layout.field, -TextImageInset, -TextImageInset);
                if (OnePixExtraBorder)
                {
                    maxBounds.Inflate(1, 1);
                }

                // Compute the final image and text bounds.
                if (ImageSize == Size.Empty || Text is null || Text.Length == 0 || textImageRelation == TextImageRelation.Overlay)
                {
                    // Do not worry about text/image overlaying
                    Size textSize = GetTextSize(maxBounds.Size);

                    // FOR EVERETT COMPATIBILITY - DO NOT CHANGE
                    Size size = ImageSize;
                    if (layout.options.EverettButtonCompat && ImageSize != Size.Empty)
                    {
                        size = new Size(size.Width + 1, size.Height + 1);
                    }

                    layout.imageBounds = LayoutUtils.Align(size, maxBounds, imageAlign);
                    layout.textBounds = LayoutUtils.Align(textSize, maxBounds, textAlign);
                }
                else
                {
                    // Rearrage text/image to prevent overlay.  Pack text into maxBounds - space reserved for image
                    Size maxTextSize = LayoutUtils.SubAlignedRegion(maxBounds.Size, ImageSize, textImageRelation);
                    Size textSize = GetTextSize(maxTextSize);
                    Rectangle maxCombinedBounds = maxBounds;

                    // Combine text & image into one rectangle that we center within maxBounds.
                    Size combinedSize = LayoutUtils.AddAlignedRegion(textSize, ImageSize, textImageRelation);
                    maxCombinedBounds.Size = LayoutUtils.UnionSizes(maxCombinedBounds.Size, combinedSize);
                    Rectangle combinedBounds = LayoutUtils.Align(combinedSize, maxCombinedBounds, ContentAlignment.MiddleCenter);

                    // imageEdge indicates whether the combination of imageAlign and textImageRelation place
                    // the image along the edge of the control.  If so, we can increase the space for text.
                    bool imageEdge = (AnchorStyles)(ImageAlignToRelation(imageAlign) & textImageRelation) != AnchorStyles.None;

                    // textEdge indicates whether the combination of textAlign and textImageRelation place
                    // the text along the edge of the control.  If so, we can increase the space for image.
                    bool textEdge = (AnchorStyles)(TextAlignToRelation(textAlign) & textImageRelation) != AnchorStyles.None;

                    if (imageEdge)
                    {
                        // If imageEdge, just split imageSize off of maxCombinedBounds.
                        LayoutUtils.SplitRegion(maxCombinedBounds, ImageSize, (AnchorStyles)textImageRelation, out layout.imageBounds, out layout.textBounds);
                    }
                    else if (textEdge)
                    {
                        // Else if textEdge, just split textSize off of maxCombinedBounds.
                        LayoutUtils.SplitRegion(maxCombinedBounds, textSize, (AnchorStyles)LayoutUtils.GetOppositeTextImageRelation(textImageRelation), out layout.textBounds, out layout.imageBounds);
                    }
                    else
                    {
                        // Expand the adjacent regions to maxCombinedBounds (centered) and split the rectangle into imageBounds and textBounds.
                        LayoutUtils.SplitRegion(combinedBounds, ImageSize, (AnchorStyles)textImageRelation, out layout.imageBounds, out layout.textBounds);
                        LayoutUtils.ExpandRegionsToFillBounds(maxCombinedBounds, (AnchorStyles)textImageRelation, ref layout.imageBounds, ref layout.textBounds);
                    }

                    // align text/image within their regions.
                    layout.imageBounds = LayoutUtils.Align(ImageSize, layout.imageBounds, imageAlign);
                    layout.textBounds = LayoutUtils.Align(textSize, layout.textBounds, textAlign);
                }

                //Don't call "layout.imageBounds = Rectangle.Intersect(layout.imageBounds, maxBounds);"
                // because that is a breaking change that causes images to be scaled to the dimensions of the control.
                //adjust textBounds so that the text is still visible even if the image is larger than the button's size

                //why do we intersect with layout.field for textBounds while we intersect with maxBounds for imageBounds?
                //this is because there are some legacy code which squeezes the button so small that text will get clipped
                //if we intersect with maxBounds. Have to do this for backward compatibility.

                if (textImageRelation == TextImageRelation.TextBeforeImage || textImageRelation == TextImageRelation.ImageBeforeText)
                {
                    //adjust the vertical position of textBounds so that the text doesn't fall off the boundary of the button
                    int textBottom = Math.Min(layout.textBounds.Bottom, layout.field.Bottom);
                    layout.textBounds.Y = Math.Max(Math.Min(layout.textBounds.Y, layout.field.Y + (layout.field.Height - layout.textBounds.Height) / 2), layout.field.Y);
                    layout.textBounds.Height = textBottom - layout.textBounds.Y;
                }
                if (textImageRelation == TextImageRelation.TextAboveImage || textImageRelation == TextImageRelation.ImageAboveText)
                {
                    //adjust the horizontal position of textBounds so that the text doesn't fall off the boundary of the button
                    int textRight = Math.Min(layout.textBounds.Right, layout.field.Right);
                    layout.textBounds.X = Math.Max(Math.Min(layout.textBounds.X, layout.field.X + (layout.field.Width - layout.textBounds.Width) / 2), layout.field.X);
                    layout.textBounds.Width = textRight - layout.textBounds.X;
                }
                if (textImageRelation == TextImageRelation.ImageBeforeText && layout.imageBounds.Size.Width != 0)
                {
                    //squeezes imageBounds.Width so that text is visible
                    layout.imageBounds.Width = Math.Max(0, Math.Min(maxBounds.Width - layout.textBounds.Width, layout.imageBounds.Width));
                    layout.textBounds.X = layout.imageBounds.X + layout.imageBounds.Width;
                }
                if (textImageRelation == TextImageRelation.ImageAboveText && layout.imageBounds.Size.Height != 0)
                {
                    //squeezes imageBounds.Height so that the text is visible
                    layout.imageBounds.Height = Math.Max(0, Math.Min(maxBounds.Height - layout.textBounds.Height, layout.imageBounds.Height));
                    layout.textBounds.Y = layout.imageBounds.Y + layout.imageBounds.Height;
                }
                //make sure that textBound is contained in layout.field
                layout.textBounds = Rectangle.Intersect(layout.textBounds, layout.field);
                if (HintTextUp)
                {
                    layout.textBounds.Y--;
                }
                if (TextOffset)
                {
                    layout.textBounds.Offset(1, 1);
                }

                // FOR EVERETT COMPATIBILITY - DO NOT CHANGE
                if (layout.options.EverettButtonCompat)
                {
                    layout.imageStart = layout.imageBounds.Location;
                    layout.imageBounds = Rectangle.Intersect(layout.imageBounds, layout.field);
                }
                else if (!Application.RenderWithVisualStyles)
                {
                    // Not sure why this is here, but we can't remove it, since it might break
                    // ToolStrips on non-themed machines
                    layout.textBounds.X++;
                }

                // clip
                //
                int bottom;
                // If we are using GDI to measure text, then we can get into a situation, where
                // the proposed height is ignore. In this case, we want to clip it against
                // maxbounds.
                if (!UseCompatibleTextRendering)
                {
                    bottom = Math.Min(layout.textBounds.Bottom, maxBounds.Bottom);
                    layout.textBounds.Y = Math.Max(layout.textBounds.Y, maxBounds.Y);
                }
                else
                {
                    // If we are using GDI+ (like Everett), then use the old Everett code
                    // This ensures that we have pixel-level rendering compatibility
                    bottom = Math.Min(layout.textBounds.Bottom, layout.field.Bottom);
                    layout.textBounds.Y = Math.Max(layout.textBounds.Y, layout.field.Y);
                }
                layout.textBounds.Height = bottom - layout.textBounds.Y;

                //This causes a breaking change because images get shrunk to the new clipped size instead of clipped.
                //********** bottom = Math.Min(layout.imageBounds.Bottom, maxBounds.Bottom);
                //********** layout.imageBounds.Y = Math.Max(layout.imageBounds.Y, maxBounds.Y);
                //********** layout.imageBounds.Height = bottom - layout.imageBounds.Y;
            }

            protected virtual Size GetTextSize(Size proposedSize)
            {
                // Set the Prefix field of TextFormatFlags
                proposedSize = LayoutUtils.FlipSizeIf(VerticalText, proposedSize);
                Size textSize = Size.Empty;

                if (UseCompatibleTextRendering)
                {
                    // GDI+ text rendering.
                    using var screen = GdiCache.GetScreenDCGraphics();
                    using StringFormat gdipStringFormat = StringFormat;
                    textSize = Size.Ceiling(
                        screen.Graphics.MeasureString(Text, Font, new SizeF(proposedSize.Width, proposedSize.Height),
                        gdipStringFormat));
                }
                else if (!string.IsNullOrEmpty(Text))
                {
                    // GDI text rendering (Whidbey feature).
                    textSize = TextRenderer.MeasureText(Text, Font, proposedSize, TextFormatFlags);
                }

                // Else skip calling MeasureText, it should return 0,0

                return LayoutUtils.FlipSizeIf(VerticalText, textSize);
            }

#if DEBUG
            public override string ToString()
            {
                return
                    "{ client = " + Client + "\n" +
                    "OnePixExtraBorder = " + OnePixExtraBorder + "\n" +
                    "borderSize = " + BorderSize + "\n" +
                    "paddingSize = " + PaddingSize + "\n" +
                    "maxFocus = " + MaxFocus + "\n" +
                    "font = " + Font + "\n" +
                    "text = " + Text + "\n" +
                    "imageSize = " + ImageSize + "\n" +
                    "checkSize = " + CheckSize + "\n" +
                    "checkPaddingSize = " + CheckPaddingSize + "\n" +
                    "checkAlign = " + CheckAlign + "\n" +
                    "imageAlign = " + ImageAlign + "\n" +
                    "textAlign = " + TextAlign + "\n" +
                    "textOffset = " + TextOffset + "\n" +
                    "shadowedText = " + ShadowedText + "\n" +
                    "textImageRelation = " + TextImageRelation + "\n" +
                    "layoutRTL = " + LayoutRTL + " }";
            }
#endif
        }
    }
}
