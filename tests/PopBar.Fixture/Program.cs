using System.Windows;
using System.Windows.Controls;
namespace PopBar.Fixture;
static class Program
{
    [STAThread] static void Main(string[] args)
    {
        var app=new Application(); var panel=new StackPanel { Margin=new Thickness(30) };
        panel.Children.Add(new TextBlock { Text="PopBar 测试文本 · 在下方选中文字",FontSize=22,Margin=new Thickness(0,0,0,20) });
        panel.Children.Add(new TextBox { Text="Hello PopBar. 这是一段用于划词测试的文字。\nhttps://example.com\n选择此处文字，按 Ctrl + Alt + Space。",AcceptsReturn=true,FontSize=20,Height=220,TextWrapping=TextWrapping.Wrap });
        panel.Children.Add(new TextBox { Text="Read-only text for selection testing.",IsReadOnly=true,FontSize=20,Height=80,Margin=new Thickness(0,20,0,0) });
        panel.Children.Add(new PasswordBox { Password="fixture-secret",Margin=new Thickness(0,20,0,0) });
        var window=new Window { Title="PopBar Test Fixture",Content=panel,Width=780,Height=530,WindowStartupLocation=WindowStartupLocation.CenterScreen };
        if(args.Length==2 && args[0]=="--verify") window.Loaded+=async (_,_)=> {
            var report=new List<string>();
            void Check(bool value,string name) { if(!value) throw new Exception(name); report.Add("PASS "+name); }
            try {
                var edit=(TextBox)panel.Children[1]; var ro=(TextBox)panel.Children[2];
                edit.Focus(); edit.Select(0,5);
                await Task.Delay(250);
                var hwnd=new System.Windows.Interop.WindowInteropHelper(window).Handle;
                Task<Selection> Read(int index) => SelectionReader.Run(()=> {
                    var root=System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                    var controls=root.FindAll(System.Windows.Automation.TreeScope.Descendants,new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty,System.Windows.Automation.ControlType.Edit));
                    return SelectionReader.ReadElement(hwnd,controls[index]);
                });
                var s=await Read(0);
                Check(s?.Text=="Hello" && s.Editable,"editable selection read: "+(s==null?SelectionReader.Diagnostic:$"text={s.Text}, editable={s.Editable}"));
                Check(await SelectionReader.Validate(s!)!=null,"unchanged selection accepted");
                Check(await SelectionReader.Run(()=>SelectionReader.BelongsToWindow(hwnd,s!.Element!)?new object():null)!=null,"selection provider belongs to source window");
                var unrelated=new Window { Title="Unrelated selection test window" };
                try {
                    var unrelatedHandle=new System.Windows.Interop.WindowInteropHelper(unrelated).EnsureHandle();
                    Check(await SelectionReader.Run(()=>SelectionReader.ReadElement(unrelatedHandle,s!.Element!))==null,"foreign window selection rejected");
                    Check(await SelectionReader.Validate(s! with { Window=unrelatedHandle })==null,"replacement rejects foreign window provider");
                } finally { unrelated.Close(); }
                edit.Select(6,6);
                Check(await SelectionReader.Validate(s!)==null,"changed selection rejected");
                edit.Select(0,0);
                Check(await Read(0)==null,"empty selection rejected");
                ro.Focus(); ro.Select(0,9);
                var readOnly=await Read(1);
                Check(readOnly?.Text=="Read-only"&&!readOnly.Editable,"read-only selection cannot replace");
                var password=(PasswordBox)panel.Children[3]; password.Focus(); password.SelectAll();
                Check(await Read(2)==null,"password selection rejected");
            } catch(Exception ex) { report.Add("FAIL "+ex.Message); }
            System.IO.File.WriteAllLines(args[1],report); app.Shutdown();
        };
        app.Run(window);
    }
}
