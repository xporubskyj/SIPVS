<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:po="http://sipvs.example.sk/purchase-order"
                exclude-result-prefixes="po">

	<xsl:output method="html" encoding="UTF-8" indent="yes"/>

	<xsl:template match="/po:purchaseOrder">
		<html>
			<head>
				<title>Objednávka</title>
				<style>
					body { font-family: Arial, sans-serif; background: #eee; }
					.paper { width: 700px; margin: 20px auto; padding: 40px; background: #fff; border: 1px solid #999; }
					h1 { text-align: center; letter-spacing: 4px; }
					table { width: 100%; border-collapse: collapse; margin-bottom: 20px; }
					th, td { border: 1px solid #000; padding: 6px; text-align: left; }
					.number { text-align: right; }
					.signature { margin-top: 60px; text-align: right; }
				</style>
			</head>
			<body>
				<div class="paper">
					<h1>OBJEDNÁVKA</h1>

					<table>
						<tr><th>Odberateľ</th><td><xsl:value-of select="po:customerName"/></td></tr>
						<tr><th>E-mail</th><td><xsl:value-of select="po:customerEmail"/></td></tr>
						<tr><th>Dátum objednávky</th><td><xsl:value-of select="po:orderDate"/></td></tr>
					</table>

					<table>
						<tr>
							<th>P. č.</th>
							<th>Popis</th>
							<th>Množstvo</th>
							<th>MJ</th>
							<th>Jednotková cena</th>
						</tr>
						<xsl:apply-templates select="po:items/po:item"/>
						<tr>
							<th colspan="4" class="number">Celková cena</th>
							<th class="number"><xsl:value-of select="po:totalPrice"/>&#160;<xsl:value-of select="@currency"/></th>
						</tr>
					</table>

					<p class="signature">..............................<br/>Podpis odberateľa</p>
				</div>
			</body>
		</html>
	</xsl:template>

	<xsl:template match="po:item">
		<tr>
			<td><xsl:value-of select="position()"/></td>
			<td><xsl:value-of select="po:description"/></td>
			<td class="number"><xsl:value-of select="po:quantity"/></td>
			<td><xsl:value-of select="po:unit"/></td>
			<td class="number"><xsl:value-of select="po:unitPrice"/></td>
		</tr>
	</xsl:template>

</xsl:stylesheet>
