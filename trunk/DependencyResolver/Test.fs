module Test

open System;
type Dummy =
    { Value : string
    }

let sayHello dummy = 
    sprintf "Hello dummy %s" dummy.Value
let d = {Value  ="Niklas"}

let changeColor clr = 
    let orig = Console.ForegroundColor
    Console.ForegroundColor <- clr
    { new IDisposable with
        member x.Dispose() =
            Console.ForegroundColor <- orig}

type Dummy with
    member x.ToMessage() = sayHello x

    member x.ToOutput() = 
        use clr = changeColor ConsoleColor.Red
        Console.WriteLine(sayHello x)



//Console.WriteLine("Hej")
//d.ToOutput()
//Console.WriteLine("Hej")
//Console.ReadKey() |> ignore


open System.IO
open System.Drawing

type ImageInfo = {
        Name : string;
        Preview : Lazy<Bitmap>
    }

let dir  = @"C:\Users\nikhal\Desktop\KanbanBoard"

let createLazyResized file = 
    lazy(use bmp = Bitmap.FromFile(file)
         let resized = new Bitmap(400, 300)
         use gr = Graphics.FromImage(resized)
         let dst = Rectangle(0, 0, 400, 300)
         let src = Rectangle(0, 0, bmp.Width, bmp.Height)
         gr.InterpolationMode <- Drawing2D.InterpolationMode.High
         gr.DrawImage(bmp, dst, src, GraphicsUnit.Pixel)
         resized)

let files = Directory.GetFiles(dir, "*.jpg") |> Array.map (fun file ->
    { Name = Path.GetFileName(file);
      Preview = createLazyResized(file)})

open System
open System.Windows.Forms

let main = new Form(Text = "Photos", ClientSize=Size(600,300))
let pict = new PictureBox(Dock=DockStyle.Fill)
let list = new ListBox()
list.Dock <- DockStyle.Left
list.Width <- 200
list.DataSource <- files
list.DisplayMember <- "Name"

list.SelectedIndexChanged.Add( fun _ ->
    let info = files.[list.SelectedIndex]
    pict.Image <- info.Preview.Value)

main.Controls.Add(pict)
main.Controls.Add(list)

[<STAThread>]
do Application.Run main