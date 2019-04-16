using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL;
using BLL;
using NNSharp.IO;
using NNSharp.Models;
using NNSharp.DataTypes;

namespace MyAIMachine
{
    public static class Machines
    {
        private static Database Db
        {
            get
            {
                return new Database();
            }
        }

        public class ElementClass
        {
            public int Id;
            public bool CalculatedInputs;
            public bool CalculatedOutputs;
        }

        public class Node
        {
            public int ElementId;
            public int Socket;  //starts from 0
            public bool IsOutput;
            public int DataType;
            public bool Calculated;
            public string Value;
        }

        //For loading and running a Machine using input data
        public class MachineRunner : IDisposable
        {
            private int Id;
            private List<Node> InputNodes;
            private List<Node> OutputNodes;
            private List<ElementClass> AllElements;
            private List<Node> AllNodes;

            //Loads a Machine from database
            //All Elements including Input and Output Terminals, and the Links between them are loaded
            private void Load(int machineId)
            {
                Machine Machine = Db.Machines.FindBy(n => n.Id == machineId).FirstOrDefault();
                foreach (var item in Db.Elements.FindBy(n => n.MachineId == machineId))
                {
                    ElementClass NewElement = new ElementClass();
                    NewElement.Id = item.Id;
                    NewElement.CalculatedInputs = item.InputNodes == 0;
                    NewElement.CalculatedOutputs = false;
                    AllElements.Add(NewElement);
                    for (int s = 0; s < item.InputNodes + item.OutputNodes; s++)
                    {
                        bool IsOutput = !(s < item.InputNodes);
                        Node NewNode = new Node();
                        NewNode.ElementId = item.Id;
                        NewNode.Socket = !IsOutput ? s : s - item.InputNodes;
                        NewNode.IsOutput = IsOutput;
                        NewNode.DataType = 1;  //Normalized Float
                        NewNode.Calculated = false;
                        AllNodes.Add(NewNode);
                        if (item.ElementType == 2 && item.InputNodes == 0)  //Input Terminal
                        {
                            Node InputNode = new Node();
                            InputNode.ElementId = item.Id;
                            InputNode.Socket = s;
                            InputNode.IsOutput = false;
                            InputNode.DataType = NewNode.DataType;
                            InputNode.Calculated = true;
                            InputNode.Value = InputNodes.Where(x => x.ElementId == item.Id && x.Socket == s).FirstOrDefault().Value;
                            AllNodes.Add(InputNode);
                        }
                    }
                }
                foreach (var item in Db.ElTerminals.GetAllMachineTerminals(machineId, true))
                    for (int i = 0; i < item.ElTerminal.Nodes; i++)
                    {
                        Node NewNode = new Node();
                        NewNode.ElementId = item.Element.Id;
                        NewNode.Socket = i;
                        NewNode.IsOutput = item.ElTerminal.IsOutput;
                        NewNode.DataType = item.ElTerminal.DataType;
                        NewNode.Calculated = false;
                        NewNode.Value = "";
                        OutputNodes.Add(NewNode);
                    }
            }

            //Loads and runs a Machine using inputNodes, and stores the results into outputNodes
            public void Run(int machineId, List<Node> inputNodes, ref List<Node> outputNodes)
            {
                InputNodes = inputNodes;
                OutputNodes = new List<Node>();
                AllElements = new List<ElementClass>();
                AllNodes = new List<Node>();
                Load(machineId);
                bool Calculating = true;
                while (Calculating && OutputNodes.Where(x => x.Calculated == false).Count() > 0)
                {
                    Calculating = false;
                    foreach (ElementClass item in AllElements.Where(x => x.CalculatedInputs && !x.CalculatedOutputs))
                    {
                        Calculating = true;
                        CalculateElement(item.Id);
                        foreach (var Link in Db.Links.FindBy(x => x.SourceElementId == item.Id))
                        {
                            Node SourceNode = AllNodes.Where(x => x.ElementId == Link.SourceElementId && x.IsOutput && x.Socket == Link.SourceSocket).FirstOrDefault();
                            Node TargetNode = AllNodes.Where(x => x.ElementId == Link.TargetElementId && !x.IsOutput && x.Socket == Link.TargetSocket).FirstOrDefault();
                            TargetNode.Value = SourceNode.Value;
                            TargetNode.Calculated = true;
                            if (AllNodes.Where(x => x.ElementId == Link.TargetElementId && !x.IsOutput && !x.Calculated).Count() == 0)
                                AllElements.Where(x => x.Id == Link.TargetElementId).FirstOrDefault().CalculatedInputs = true;
                            if (OutputNodes.Where(x => x.ElementId == TargetNode.ElementId && x.Socket == TargetNode.Socket).Count() == 1)
                            {
                                Node OutputNode = OutputNodes.Where(x => x.ElementId == TargetNode.ElementId && x.Socket == TargetNode.Socket).FirstOrDefault();
                                OutputNode.Value = TargetNode.Value;
                                OutputNode.Calculated = true;
                            }
                        }

                    }
                }
                outputNodes = OutputNodes;
            }

            //Executes an Element
            //Calculates output values of an Element based on input values loaded on it and the element type
            private void CalculateElement(int Id)
            {
                ElementClass ElementToCalculate = AllElements.Where(x => x.Id == Id).FirstOrDefault();
                Element Element = Db.Elements.FindBy(n => n.Id == Id).FirstOrDefault();
                int InputSockets = Element.ElementType != 2 ? Element.InputNodes : Element.OutputNodes;
                int OutputSockets = Element.OutputNodes;
                string[] InputValues = new string[InputSockets];
                string[] OutputValues = new string[OutputSockets];
                for (int s = 0; s < InputSockets; s++)
                    InputValues[s] = AllNodes.Where(x => x.ElementId == Id && !x.IsOutput && x.Socket == s).FirstOrDefault().Value;
                switch (Element.ElementType)
                {
                    case 1:  //Neural Network
                        OutputValues = Calculate.NeuralNetwork(Element, InputValues);
                        break;
                    case 2:  //Terminal
                        for (int s = 0; s < OutputSockets; s++)
                            OutputValues[s] = InputValues[s];
                        break;
                }
                for (int s = 0; s < OutputSockets; s++)
                {
                    Node NodeToCalculate = AllNodes.Where(x => x.ElementId == Id && x.IsOutput && x.Socket == s).FirstOrDefault();
                    NodeToCalculate.Value = OutputValues[s];
                    NodeToCalculate.Calculated = true;
                }
                ElementToCalculate.CalculatedOutputs = true;
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {
                    // free managed resources
                }
                // free native resources if there are any.
            }
        }

        private static class Calculate
        {
            //Calculate output values of a Neural Network Element based on inputValues
            public static string[] NeuralNetwork(Element element, string[] inputValues)
            {
                // Read the previously created json model
                var reader = new ReaderKerasModel(Constants.ModelsFolder + @"\" + element.Id + ".json");

                SequentialModel model = reader.GetSequentialExecutor();

                // Create the data to run the executer on
                Data2D input = new Data2D(1, 1, element.InputNodes, 1);
                for (int i = 0; i < element.InputNodes; i++)
                {
                    double inputValue = 0;
                    double.TryParse(inputValues[i], out inputValue);
                    input[0, 0, i, 0] = inputValue;
                }

                // Calculate the network's output.
                IData output = model.ExecuteNetwork(input);

                string[] OutputValues = new string[element.OutputNodes];
                for (int o = 0; o < element.OutputNodes; o++)
                    OutputValues[o] = ((Data2D)output)[0, 0, o, 0].ToString("F6");

                return OutputValues;
            }
        }

    }
}