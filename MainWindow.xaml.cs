using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace GCodeGenerator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Input.Text = InstructionGenerator.Characters.Keys.Last().ToString();
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            float left = 0;
            float top = 0;
            InstructionGenerator.TopLeft = new Vector2(left, top);
            InstructionGenerator.FontSize = 36;
            InstructionGenerator.FontRadius = 0.05;
            InstructionGenerator.FontSpacing = 0.2;
            InstructionGenerator.ArcInterpolationDistance = 0.1;
            InstructionGenerator.Speed = 4000;
            StringBuilder output = new StringBuilder();
            output.Append(InstructionGenerator.Initialize());

            //foreach (float fontSize in new float[] { 10, 15, 20, 25, 30 })
            //{
            //    InstructionGenerator.FontSize = fontSize;
            //    output.Append(new string($"{fontSize:0} Checkbox".SelectMany(c => InstructionGenerator.Execute(c)).ToArray()));
            //    InstructionGenerator.TopLeft = new Vector2(left, InstructionGenerator.TopLeft.Y - (1.5f * fontSize) - InstructionGenerator.FontSpacing * fontSize);
            //}

            foreach (char c in Input.Text)
            {
                switch (c)
                {
                    case '\r':
                        InstructionGenerator.TopLeft = new Vector2(left, InstructionGenerator.TopLeft.Y);
                        break;
                    case '\n':
                        InstructionGenerator.TopLeft = new Vector2(InstructionGenerator.TopLeft.X, InstructionGenerator.TopLeft.Y - (1.5f * InstructionGenerator.FontSize) - InstructionGenerator.FontSpacing * InstructionGenerator.FontSize);
                        break;
                    default:
                        output.Append(InstructionGenerator.Execute(c));
                        break;
                }
            }

            //output.Append(InstructionGenerator.GoHome());

            Output.Text = output.ToString();
        }
    }
}
