using ProjetoJP.Controles;
using ProjetoJP.Shared;
using QRCoder;

namespace ProjetoJP.Pdv.Telas;

/// <summary>
/// Pagamento em Pix: mostra o QR Code com o valor (Pix estático, direto para a chave da loja) e espera o operador
/// confirmar que o Pix chegou. OK = recebido; Cancelar = volta para o pagamento sem lançar nada.
/// No modo teste (Opções do caixa → Pix) só mostra o QR, sem o botão de recebido.
/// </summary>
public class PixQrForm : Form
{
    private const int LadoQr = 260;

    public PixQrForm(PdvConfig cfg, decimal valor, bool teste = false)
    {
        var chave = cfg.PixChaveNormalizada ?? "";
        var codigo = Pix.Codigo(chave, cfg.PixRecebedor, cfg.PixCidade, valor);
        Ui.Dialogo(this, teste ? "Pix — QR Code de teste" : "Pagamento em Pix", 480, 534);

        var rotulo = new Label
        {
            Text = teste ? "QR CODE DE TESTE — NÃO PRECISA PAGAR" : "PIX — VALOR A RECEBER",
            Font = Tema.Semibold(9.5f), ForeColor = Tema.TextoSuave, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter
        };
        rotulo.SetBounds(20, 14, 440, 22);
        var valorTexto = new Label
        {
            Text = Ui.Dinheiro(valor), Font = Tema.Semibold(24), ForeColor = Tema.Texto, AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter
        };
        valorTexto.SetBounds(20, 36, 440, 46);

        // O QR fica sempre em fundo branco (também no tema escuro): o celular lê melhor.
        var fundoQr = new Panel { BackColor = Color.White };
        fundoQr.SetBounds((480 - (LadoQr + 24)) / 2, 88, LadoQr + 24, LadoQr + 24);
        var qr = new PictureBox { SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.White };
        qr.SetBounds(12, 12, LadoQr, LadoQr);
        try { qr.Image = GerarQr(codigo); }
        catch (Exception ex) { Ui.Erro(this, "Não foi possível gerar o QR Code: " + ex.Message); }
        fundoQr.Controls.Add(qr);

        var recebedor = new Label
        {
            Text = "Recebedor: " + Pix.Limpar(cfg.PixRecebedor, 25), Font = Tema.Fonte(9.5f), ForeColor = Tema.Texto,
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true
        };
        recebedor.SetBounds(20, 380, 440, 20);
        var chaveTexto = new Label
        {
            Text = $"Chave ({Pix.Nome(cfg.PixTipo)}): {chave}", Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave,
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true
        };
        chaveTexto.SetBounds(20, 400, 440, 20);
        var instrucao = new Label
        {
            Text = teste
                ? "Leia com o celular: o app do banco deve mostrar o nome da loja e R$ 1,00. Não é preciso pagar."
                : "Peça para o cliente ler o QR Code no app do banco. Confira no app ou no extrato que o Pix chegou antes de confirmar.",
            Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter
        };
        instrucao.SetBounds(20, 424, 440, 40);

        var copiar = Ui.Botao("Copiar código", Ui.Neutro, 130);
        var recebido = Ui.Botao("Recebido   (Enter)", Ui.Sucesso, 170);
        var cancelar = Ui.Botao(teste ? "Fechar   (Esc)" : "Cancelar   (Esc)", Ui.Neutro, 140);
        copiar.Location = new Point(20, 476);
        recebido.Location = new Point(160, 476);
        cancelar.Location = new Point(340, 476);
        copiar.Click += (_, _) =>
        {
            // "Pix copia e cola": o cliente pode receber o código por mensagem e colar no app do banco.
            try { Clipboard.SetText(codigo); copiar.Text = "Copiado"; }
            catch (Exception) { Ui.Aviso(this, "Não foi possível copiar agora. Tente de novo."); }
        };
        recebido.Click += (_, _) => DialogResult = DialogResult.OK;
        cancelar.DialogResult = DialogResult.Cancel;
        CancelButton = cancelar;

        Controls.AddRange([rotulo, valorTexto, fundoQr, recebedor, chaveTexto, instrucao, copiar, cancelar]);
        if (teste) return;

        Controls.Add(recebido);
        // O Enter que abriu esta janela não pode confirmar o Pix sozinho: o botão só vale depois de um instante.
        recebido.Enabled = false;
        var espera = new System.Windows.Forms.Timer { Interval = 800 };
        espera.Tick += (_, _) =>
        {
            espera.Stop();
            espera.Dispose();
            if (IsDisposed) return;
            recebido.Enabled = true;
            AcceptButton = recebido;
            recebido.Focus();
        };
        Shown += (_, _) => espera.Start();
    }

    /// <summary>QR com módulos inteiros (nítido, sem borrar ao ampliar), do maior tamanho que cabe no quadro.</summary>
    private static Bitmap GerarQr(string codigo)
    {
        using var gerador = new QRCodeGenerator();
        using var dados = gerador.CreateQrCode(codigo, QRCodeGenerator.ECCLevel.M);
        var pixelsPorModulo = Math.Max(2, LadoQr / Math.Max(1, dados.ModuleMatrix.Count));
        var png = new PngByteQRCode(dados).GetGraphic(pixelsPorModulo);
        using var ms = new MemoryStream(png);
        using var imagem = Image.FromStream(ms);
        return new Bitmap(imagem);
    }
}
