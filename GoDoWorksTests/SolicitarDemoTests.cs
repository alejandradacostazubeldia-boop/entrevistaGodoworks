using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace GoDoWorksTests;

#region Configuración y cierre de los tests
[TestFixture]
public class SolicitarDemoTests
{
    private IWebDriver _driver = null!;
    private WebDriverWait _wait = null!;

    private const string UrlSoluciones = "https://www.godoworks.com/soluciones/";
    private const string MensajeEsperado = "Faltan datos. Revisá los campos marcados";

    [SetUp]
    public void SetUp()
    {
        var options = new ChromeOptions();
        options.AddArgument("--start-maximized");

        // Selenium Manager descarga el chromedriver automáticamente
        _driver = new ChromeDriver(options);
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(15));
    }

    [TearDown]
    public void TearDown()
    {
        // Si el test falla, guarda una captura como evidencia
        if (TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed)
        {
            var path = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                $"error_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            ((ITakesScreenshot)_driver).GetScreenshot().SaveAsFile(path);
            TestContext.AddTestAttachment(path);
            TestContext.Out.WriteLine($"Captura guardada en: {path}");
            TestContext.Out.WriteLine($"URL al fallar: {_driver.Url}");
        }

        _driver.Quit();
        _driver.Dispose();
    }
    #endregion


    #region Test principal
    [Test]
    public void EnviarFormularioConEmpresaVacio_MuestraMensajeDeValidacion()
    {
        // 1. Abrir la URL en Chrome
        _driver.Navigate().GoToUrl(UrlSoluciones);

        // 2. Click en GDW Health (tarjeta que lleva a /gdw-health/)
        var tarjeta = ScrollHastaEncontrar("//a[contains(@class,'sol__link') and contains(@href,'gdw-health')]");
        ClickElemento(tarjeta);
        _wait.Until(d => d.Url.Contains("/gdw-health"));

        // 3. Click en "Solicitar una demostración" (ya en la página de GDW Health)
        var botonDemo = ScrollHastaEncontrar("//a[contains(@class,'btn') and @href='#contacto']");
        ClickElemento(botonDemo);
        Assert.That(_driver.Url, Does.EndWith("#contacto"), "El botón no llevó a la sección del formulario.");

        // 4. Verificar que se llega al formulario y que queda visible en pantalla
        var campoNombre = CampoPorEtiqueta("Nombre completo");
        Assert.That(campoNombre.Displayed, Is.True, "El formulario no se muestra.");

        var enPantalla = false;
        try
        {
            // Se espera porque el desplazamiento puede ser animado
            enPantalla = _wait.Until(_ => EstaEnViewport(campoNombre));
        }
        catch (WebDriverTimeoutException)
        {
        }
        Assert.That(enPantalla, Is.True, "La página no se desplazó hasta el formulario.");

        // 5. Completar los campos (Empresa se deja vacío a propósito)
        var campoEmpresa = CampoPorEtiqueta("Nombre de la empresa", "Empresa");
        var bordeAntes = campoEmpresa.GetCssValue("border-color");

        campoNombre.SendKeys("John Smith Prueba");
        CampoPorEtiqueta("Correo electrónico").SendKeys("info@godoworks.com");
        CampoPorEtiqueta("Telefono de contacto", "Teléfono de contacto", "Teléfono").SendKeys("59895654785");
        // Empresa: vacío
        CampoPorEtiqueta("Cargo o posición", "Cargo").SendKeys("Analista QA + IA");
        CampoPorEtiqueta("Ciudad/País", "Ciudad").SendKeys("Uruguay");

        MarcarCaptcha();

        // 6. Click en "Enviar consulta"
        var botonEnviar = EsperarElemento(
            "//*[self::button or self::a or self::input]" +
            "[contains(normalize-space(.), 'Enviar consulta') or contains(@value, 'Enviar consulta')]");
        ClickElemento(botonEnviar);

        // 7. Verificar el mensaje de error
        var banner = _wait.Until(d =>
            d.FindElements(By.XPath($"//*[contains(normalize-space(.), '{MensajeEsperado}')]"))
             .LastOrDefault(e => e.Displayed));

        Assert.That(banner, Is.Not.Null, "No apareció el mensaje de validación.");
        Assert.That(banner!.Text, Does.Contain(MensajeEsperado));

        // El campo Empresa debe quedar marcado (el borde cambia respecto del estado inicial)
        var bordeDespues = campoEmpresa.GetCssValue("border-color");
        Assert.That(bordeDespues, Is.Not.EqualTo(bordeAntes),
            "El campo Empresa no quedó resaltado.");
    }
    #endregion

    #region Métodos auxiliares

    // Baja la página de a tramos hasta que aparece el elemento (útil con carga diferida)
private IWebElement ScrollHastaEncontrar(string xpath)
{
    var js = (IJavaScriptExecutor)_driver;
    var fin = DateTime.Now.AddSeconds(40);
    var intentosEnElFinal = 0;

    while (DateTime.Now < fin)
    {
        var el = _driver.FindElements(By.XPath(xpath)).FirstOrDefault();
        if (el != null)
        {
            js.ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", el);
            Thread.Sleep(500);
            return el;
        }

        var llegoAlFinal = js.ExecuteScript(@"
            window.scrollBy(0, Math.floor(window.innerHeight * 0.7));
            return (window.innerHeight + window.scrollY) >= document.documentElement.scrollHeight - 5;") is true;

        Thread.Sleep(500);

        // En el final de la página se dan unas vueltas más por si aún carga contenido
        if (llegoAlFinal && ++intentosEnElFinal >= 4)
            break;
    }

    throw new NoSuchElementException($"No se encontró el elemento tras recorrer la página: {xpath}");
}

    // Espera a que exista el elemento; devuelve el primero visible (o el primero encontrado)
    private IWebElement EsperarElemento(string xpath)
    {
        return _wait.Until(d =>
        {
            var encontrados = d.FindElements(By.XPath(xpath));
            return encontrados.FirstOrDefault(e => e.Displayed) ?? encontrados.FirstOrDefault();
        })!;
    }

    // Scroll + click normal; si algo lo tapa o no se considera visible, click por JavaScript
    private void ClickElemento(IWebElement el)
    {
        var js = (IJavaScriptExecutor)_driver;
        js.ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", el);

        try
        {
            _wait.Until(_ => el.Displayed && el.Enabled);
            el.Click();
        }
        catch (Exception ex) when (ex is WebDriverTimeoutException
                                      or ElementClickInterceptedException
                                      or ElementNotInteractableException)
        {
            js.ExecuteScript("arguments[0].click();", el);
        }
    }

    // Busca el input/textarea que sigue a una etiqueta con alguno de los textos indicados
    private IWebElement CampoPorEtiqueta(params string[] textos)
    {
        var condicion = string.Join(" or ",
            textos.Select(t => $"contains(normalize-space(text()), '{t}')"));

        var xpath = $"//*[self::label or self::span or self::div or self::p][{condicion}]" +
                    "/following::*[self::input or self::textarea][not(@type='hidden')][1]";

        return _wait.Until(d =>
            d.FindElements(By.XPath(xpath)).FirstOrDefault(e => e.Displayed))!;
    }

    private bool EstaEnViewport(IWebElement el)
    {
        return ((IJavaScriptExecutor)_driver).ExecuteScript(@"
            var r = arguments[0].getBoundingClientRect();
            return r.top >= 0 && r.bottom <= (window.innerHeight || document.documentElement.clientHeight);",
            el) is true;
    }

    private void MarcarCaptcha()
    {
        // Suponiendo reCAPTCHA ("Soy humano") dentro de un iframe.
        // Revisar el selector real con DevTools.
        try
        {
            var iframe = _driver.FindElement(By.CssSelector("iframe[title*='reCAPTCHA'], iframe[src*='recaptcha']"));
            _driver.SwitchTo().Frame(iframe);
            _driver.FindElement(By.CssSelector(".recaptcha-checkbox-border")).Click();
        }
        catch (NoSuchElementException)
        {
            TestContext.Out.WriteLine("No se encontró el captcha; se continúa sin marcarlo.");
        }
        finally
        {
            _driver.SwitchTo().DefaultContent();
        }
    }
    #endregion
}