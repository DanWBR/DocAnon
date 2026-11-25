using DocAnon.Core.Detection;

namespace DocAnon.Core.Tests;

public class ValidatorTests
{
    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("529.982.247-26", false)]
    [InlineData("111.111.111-11", false)]
    public void Cpf(string value, bool valid) => Assert.Equal(valid, Validators.Cpf(value));

    [Theory]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11.222.333/0001-82", false)]
    [InlineData("12.ABC.345/01DE-35", true)]
    [InlineData("12.ABC.345/01DE-36", false)]
    [InlineData("12ABC34501DE35", true)]
    public void Cnpj(string value, bool valid) => Assert.Equal(valid, Validators.Cnpj(value));

    [Theory]
    [InlineData("4111 1111 1111 1111", true)]
    [InlineData("5500-0000-0000-0004", true)]
    [InlineData("4111 1111 1111 1112", false)]
    [InlineData("1234 5678 9012 3452", false)]
    public void CreditCard(string value, bool valid) => Assert.Equal(valid, Validators.CreditCard(value));

    [Fact]
    public void PisFromGenerator()
    {
        string pis = Fake.Pis("1203456789");
        Assert.True(Validators.Pis(pis));
        Assert.False(Validators.Pis(pis[..^1] + ((pis[^1] - '0' + 1) % 10)));
    }

    [Fact]
    public void CnjFromGenerator()
    {
        string cnj = Fake.Cnj("0001234", "20248260100");
        Assert.True(Validators.ProcessoCnj(cnj));
        Assert.False(Validators.ProcessoCnj(cnj.Replace("2024", "2023")));
    }

    [Theory]
    [InlineData("GB82 WEST 1234 5698 7654 32", true)]
    [InlineData("GB82 WEST 1234 5698 7654 33", false)]
    public void Iban(string value, bool valid) => Assert.Equal(valid, Validators.Iban(value));

    [Theory]
    [InlineData("192.168.0.10", true)]
    [InlineData("256.1.1.1", false)]
    [InlineData("1.02.3.4", false)]
    public void IPv4(string value, bool valid) => Assert.Equal(valid, Validators.IPv4(value));
}
