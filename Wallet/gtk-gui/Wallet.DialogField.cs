
// This file has been generated by the GUI designer. Do not modify.
namespace Wallet
{
	public partial class DialogField
	{
		private global::Gtk.HBox hbox1;

		private global::Gtk.Label label;

		private global::Gtk.Entry entry;

		private global::Gtk.Label label2;

		protected virtual void Build()
		{
			global::Stetic.Gui.Initialize(this);
			// Widget Wallet.DialogField
			global::Stetic.BinContainer.Attach(this);
			this.Name = "Wallet.DialogField";
			// Container child Wallet.DialogField.Gtk.Container+ContainerChild
			this.hbox1 = new global::Gtk.HBox();
			this.hbox1.Name = "hbox1";
			this.hbox1.Spacing = 6;
			// Container child hbox1.Gtk.Box+BoxChild
			this.label = new global::Gtk.Label();
			this.label.WidthRequest = 100;
			this.label.Name = "label";
			this.label.Xpad = 10;
			this.label.Xalign = 1F;
			this.label.LabelProp = global::Mono.Unix.Catalog.GetString("Caption");
			this.hbox1.Add(this.label);
			global::Gtk.Box.BoxChild w1 = ((global::Gtk.Box.BoxChild)(this.hbox1[this.label]));
			w1.Position = 0;
			w1.Expand = false;
			w1.Fill = false;
			// Container child hbox1.Gtk.Box+BoxChild
			this.entry = new global::Gtk.Entry();
			this.entry.HeightRequest = 35;
			this.entry.CanFocus = true;
			this.entry.Name = "entry";
			this.entry.IsEditable = true;
			this.entry.HasFrame = false;
			this.entry.InvisibleChar = '●';
			this.hbox1.Add(this.entry);
			global::Gtk.Box.BoxChild w2 = ((global::Gtk.Box.BoxChild)(this.hbox1[this.entry]));
			w2.Position = 1;
			// Container child hbox1.Gtk.Box+BoxChild
			this.label2 = new global::Gtk.Label();
			this.label2.WidthRequest = 100;
			this.label2.Name = "label2";
			this.hbox1.Add(this.label2);
			global::Gtk.Box.BoxChild w3 = ((global::Gtk.Box.BoxChild)(this.hbox1[this.label2]));
			w3.Position = 2;
			w3.Expand = false;
			w3.Fill = false;
			this.Add(this.hbox1);
			if ((this.Child != null))
			{
				this.Child.ShowAll();
			}
			this.Hide();
		}
	}
}