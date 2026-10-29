using System;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Nixue.Core.Diagnostics;

namespace Nixue.App.UI
{
    /// <summary>
    /// Builds a modern, flat dark control theme (buttons, text boxes, check boxes,
    /// sliders, combo boxes) as implicit styles. The templates are authored as XAML and
    /// parsed at runtime with <see cref="XamlReader"/> - far less error-prone than
    /// hand-building ControlTemplates in code - with the active theme's colours injected.
    /// Merging the result into a window's <c>Resources</c> restyles every matching
    /// control it contains.
    /// </summary>
    public static class ModernStyles
    {
        public static void Apply(FrameworkElement scope, ThemeResources theme)
        {
            try
            {
                ResourceDictionary rd = Build(theme);
                scope.Resources.MergedDictionaries.Add(rd);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not apply modern styles: " + ex.Message);
            }
        }

        public static ResourceDictionary Build(ThemeResources t)
        {
            string xaml = Template
                .Replace("$FG$", Hex(t.Foreground))
                .Replace("$SFG$", Hex(t.SecondaryForeground))
                .Replace("$BG$", Hex(t.TerminalBackground))
                .Replace("$PANEL$", Hex(t.SecondaryPanel))
                .Replace("$BORDER$", Hex(t.Border))
                .Replace("$ACCENT$", Hex(t.Accent))
                .Replace("$HOVER$", HexA(t.Accent, 0x2A))
                .Replace("$ONACCENT$", "#0A0A0A");
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }

        private static string Hex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        private static string HexA(Color c, int a)
        {
            return "#" + a.ToString("X2") + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        // Single-quoted XAML so it nests cleanly in a C# verbatim string.
        private const string Template = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                     xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <Style TargetType='Button'>
    <Setter Property='Foreground' Value='$FG$'/>
    <Setter Property='Background' Value='$PANEL$'/>
    <Setter Property='BorderBrush' Value='$BORDER$'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='12,6'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='4' SnapsToDevicePixels='True'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='b' Property='BorderBrush' Value='$ACCENT$'/>
              <Setter TargetName='b' Property='Background' Value='$HOVER$'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='b' Property='Background' Value='$ACCENT$'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='TextBox'>
    <Setter Property='Foreground' Value='$FG$'/>
    <Setter Property='Background' Value='$BG$'/>
    <Setter Property='BorderBrush' Value='$BORDER$'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='6,4'/>
    <Setter Property='CaretBrush' Value='$ACCENT$'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='3'>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='b' Property='BorderBrush' Value='$ACCENT$'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='$FG$'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Border x:Name='box' Width='17' Height='17' CornerRadius='3' VerticalAlignment='Center'
                    Background='$BG$' BorderBrush='$BORDER$' BorderThickness='1'>
              <Path x:Name='check' Stroke='$ONACCENT$' StrokeThickness='2' Visibility='Collapsed'
                    StrokeStartLineCap='Round' StrokeEndLineCap='Round' Data='M 3.5,8.5 L 7,12 L 13,4.5'/>
            </Border>
            <ContentPresenter Margin='9,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='box' Property='Background' Value='$ACCENT$'/>
              <Setter TargetName='box' Property='BorderBrush' Value='$ACCENT$'/>
              <Setter TargetName='check' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='box' Property='BorderBrush' Value='$ACCENT$'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='Slider'>
    <Setter Property='Height' Value='24'/>
    <Setter Property='Foreground' Value='$ACCENT$'/>
    <Setter Property='IsMoveToPointEnabled' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Slider'>
          <Grid VerticalAlignment='Center'>
            <Border Height='4' CornerRadius='2' Background='$BORDER$' VerticalAlignment='Center'/>
            <Track x:Name='PART_Track'>
              <Track.Thumb>
                <Thumb x:Name='thumb' Width='15' Height='15' Cursor='Hand'>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Grid>
                        <Ellipse Fill='$ACCENT$'/>
                      </Grid>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='$FG$'/>
    <Setter Property='Background' Value='$BG$'/>
    <Setter Property='BorderBrush' Value='$BORDER$'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Height' Value='28'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <ToggleButton Focusable='False' ClickMode='Press'
                          IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
              <ToggleButton.Template>
                <ControlTemplate TargetType='ToggleButton'>
                  <Border x:Name='bd' Background='$BG$' BorderBrush='$BORDER$' BorderThickness='1' CornerRadius='3'>
                    <Grid>
                      <Grid.ColumnDefinitions>
                        <ColumnDefinition Width='*'/>
                        <ColumnDefinition Width='24'/>
                      </Grid.ColumnDefinitions>
                      <Path Grid.Column='1' HorizontalAlignment='Center' VerticalAlignment='Center'
                            Fill='$SFG$' Data='M 0,0 L 9,0 L 4.5,5 Z'/>
                    </Grid>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'>
                      <Setter TargetName='bd' Property='BorderBrush' Value='$ACCENT$'/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Content='{TemplateBinding SelectionBoxItem}' Margin='9,0,26,0'
                              VerticalAlignment='Center' IsHitTestVisible='False'/>
            <Popup x:Name='PART_Popup' Placement='Bottom' AllowsTransparency='True' Focusable='False'
                   IsOpen='{TemplateBinding IsDropDownOpen}' PopupAnimation='Fade'>
              <Border Background='$PANEL$' BorderBrush='$BORDER$' BorderThickness='1' CornerRadius='3'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='260'>
                <ScrollViewer><ItemsPresenter/></ScrollViewer>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='$FG$'/>
    <Setter Property='Padding' Value='9,6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'>
          <Border x:Name='bd' Background='Transparent' Padding='{TemplateBinding Padding}'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='bd' Property='Background' Value='$HOVER$'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>";
    }
}
