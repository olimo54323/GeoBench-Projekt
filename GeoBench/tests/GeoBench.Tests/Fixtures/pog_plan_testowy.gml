<?xml version="1.0" encoding="utf-8"?><wfs:FeatureCollection xmlns:app="https://www.gov.pl/static/zagospodarowanieprzestrzenne/schemas/app/2.0" xmlns:gco="http://www.isotc211.org/2005/gco" xmlns:gmd="http://www.isotc211.org/2005/gmd" xmlns:gml="http://www.opengis.net/gml/3.2" xmlns:wfs="http://www.opengis.net/wfs/2.0" xmlns:xlink="http://www.w3.org/1999/xlink" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" timeStamp="2026-08-26T07:37:43Z" numberReturned="5" numberMatched="unknown" xsi:schemaLocation="https://www.gov.pl/static/zagospodarowanieprzestrzenne/schemas/app/2.0 https://www.gov.pl/static/zagospodarowanieprzestrzenne/schemas/app/2.0/planowaniePrzestrzenne_2_0.xsd http://www.opengis.net/gml/3.2 http://schemas.opengis.net/gml/3.2.1/gml.xsd http://www.opengis.net/wfs/2.0 http://schemas.opengis.net/wfs/2.0/wfs.xsd">
    <!-- Akt testowy (przestrzeń nazw PL.ZIPPZP.TEST, żeby test nie zastąpił prawdziwego planu); zasięg to prostokąt obejmujący strefy -->
    <wfs:member>
        <app:AktPlanowaniaPrzestrzennego gml:id="PL.ZIPPZP.TEST_246401-POG_1POG_20260714T101200">
            <app:idIIP>
                <app:Identyfikator>
                    <app:przestrzenNazw>PL.ZIPPZP.TEST/246401-POG</app:przestrzenNazw>
                    <app:lokalnyId>1POG</app:lokalnyId>
                    <app:wersjaId>20260714T101200</app:wersjaId>
                </app:Identyfikator>
            </app:idIIP>
            <app:poczatekWersjiObiektu>2026-07-14T10:12:00Z</app:poczatekWersjiObiektu>
            <app:tytul>Plan ogólny gminy Częstochowa (fragment testowy)</app:tytul>
            <app:tytulAlternatywny>POG miasta Częstochowy</app:tytulAlternatywny>
            <app:obowiazujeOd>2026-07-14</app:obowiazujeOd>
            <app:status xlink:href="http://inspire.ec.europa.eu/codelist/ProcessStepGeneralValue/elaboration" xlink:title="w opracowaniu"/>
            <app:zasiegPrzestrzenny>
                <gml:MultiSurface srsDimension="2" srsName="http://www.opengis.net/def/crs/EPSG/0/2177">
                    <gml:surfaceMember>
                        <gml:Polygon>
                            <gml:exterior>
                                <gml:LinearRing>
                                    <gml:posList>5628000 6582000 5628000 6585900 5634500 6585900 5634500 6582000 5628000 6582000</gml:posList>
                                </gml:LinearRing>
                            </gml:exterior>
                        </gml:Polygon>
                    </gml:surfaceMember>
                </gml:MultiSurface>
            </app:zasiegPrzestrzenny>
        </app:AktPlanowaniaPrzestrzennego>
    </wfs:member>
    <wfs:member>
        <app:StrefaPlanistyczna gml:id="PL.ZIPPZP.TEST_246401-POG_1POG-1SC_20250704T112613">
            <gml:identifier codeSpace="https://www.gov.pl/zagospodarowanieprzestrzenne/app">https://www.gov.pl/zagospodarowanieprzestrzenne/app/StrefaPlanistyczna/PL.ZIPPZP.TEST/246401-POG/1POG-1SC/20250704T112613</gml:identifier>
            <app:idIIP>
                <app:Identyfikator>
                    <app:przestrzenNazw>PL.ZIPPZP.TEST/246401-POG</app:przestrzenNazw>
                    <app:lokalnyId>1POG-1SC</app:lokalnyId>
                    <app:wersjaId>20250704T112613</app:wersjaId>
                </app:Identyfikator>
            </app:idIIP>
            <app:oznaczenie>1SC</app:oznaczenie>
            <app:symbol>SC</app:symbol>
            <app:poczatekWersjiObiektu>2025-07-04T11:26:13Z</app:poczatekWersjiObiektu>
            <app:obowiazujeOd>2025-07-04</app:obowiazujeOd>
            <app:status xlink:href="http://inspire.ec.europa.eu/codelist/ProcessStepGeneralValue/elaboration" xlink:title="w opracowaniu"/>
            <app:charakterUstalenia xlink:href="https://inspire.ec.europa.eu/codelist/RegulationNatureValue/generallyBinding" xlink:title="ogólnie wiążące"/>
            <app:geometria>
                <gml:Polygon srsDimension="2" srsName="http://www.opengis.net/def/crs/EPSG/0/2177">
                    <gml:exterior>
                        <gml:LinearRing>
                            <gml:posList>5632457.44 6585875.14 5632460.3 6585774.23 5632578.59184886 6585768.61886509 5632573.54920907 6585872.83342081 5632457.44 6585875.14</gml:posList>
                        </gml:LinearRing>
                    </gml:exterior>
                </gml:Polygon>
            </app:geometria>
            <app:plan xlink:href="https://www.gov.pl/zagospodarowanieprzestrzenne/app/AktPlanowaniaPrzestrzennego/PL.ZIPPZP.TEST/246401-POG/1POG"/>
            <app:nazwa xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/codelist/RodzajStrefyPlanistycznejKod/strefaCmentarzy" xlink:title="strefa cmentarzy"/>
            <app:profilPodstawowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-C" xlink:title="teren cmentarza"/>
            <app:profilPodstawowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-K" xlink:title="teren komunikacji"/>
            <app:profilPodstawowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-ZP" xlink:title="teren zieleni urządzonej"/>
            <app:profilPodstawowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-ZD" xlink:title="teren ogrodów działkowych"/>
            <app:profilPodstawowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-I" xlink:title="teren infrastruktury technicznej"/>
            <app:profilDodatkowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-UR" xlink:title="teren usług kultu religijnego"/>
            <app:profilDodatkowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-ZN" xlink:title="teren zieleni naturalnej"/>
            <app:profilDodatkowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-L" xlink:title="teren lasu"/>
            <app:maksNadziemnaIntensywnoscZabudowy>0.1</app:maksNadziemnaIntensywnoscZabudowy>
            <app:maksUdzialPowierzchniZabudowy>10.0</app:maksUdzialPowierzchniZabudowy>
            <app:maksWysokoscZabudowy uom="m">10.0</app:maksWysokoscZabudowy>
            <app:minUdzialPowierzchniBiologicznieCzynnej>25.0</app:minUdzialPowierzchniBiologicznieCzynnej>
        </app:StrefaPlanistyczna>
    </wfs:member>
    <wfs:member>
        <app:StrefaPlanistyczna gml:id="PL.ZIPPZP.TEST_246401-POG_1POG-70SN_20250711T090526">
            <gml:identifier codeSpace="https://www.gov.pl/zagospodarowanieprzestrzenne/app">https://www.gov.pl/zagospodarowanieprzestrzenne/app/StrefaPlanistyczna/PL.ZIPPZP.TEST/246401-POG/1POG-70SN/20250711T090526</gml:identifier>
            <app:idIIP>
                <app:Identyfikator>
                    <app:przestrzenNazw>PL.ZIPPZP.TEST/246401-POG</app:przestrzenNazw>
                    <app:lokalnyId>1POG-70SN</app:lokalnyId>
                    <app:wersjaId>20250711T090526</app:wersjaId>
                </app:Identyfikator>
            </app:idIIP>
            <app:oznaczenie>70SN</app:oznaczenie>
            <app:symbol>SN</app:symbol>
            <app:poczatekWersjiObiektu>2025-07-11T09:05:26Z</app:poczatekWersjiObiektu>
            <app:obowiazujeOd>2025-07-11</app:obowiazujeOd>
            <app:status xlink:href="http://inspire.ec.europa.eu/codelist/ProcessStepGeneralValue/elaboration" xlink:title="w opracowaniu"/>
            <app:charakterUstalenia xlink:href="https://inspire.ec.europa.eu/codelist/RegulationNatureValue/generallyBinding" xlink:title="ogólnie wiążące"/>
            <app:geometria>
                <gml:Polygon srsDimension="2" srsName="http://www.opengis.net/def/crs/EPSG/0/2177">
                    <gml:exterior>
                        <gml:LinearRing>
                            <gml:posList>5628305.68 6583398.57 5628246.1 6583487.19 5628220.67 6583478.35 5628125.01 6583413.57 5628019.13 6583341.89 5628013.36 6583328.29 5628014.54 6583321.55 5628024.91 6583284.74 5628037.73 6583255.48 5628050.22 6583240.94 5628076.68 6583220.87 5628109.88 6583203.59 5628143.89 6583195.95 5628204.15557902 6583182.38951723 5628247.44 6583172.65 5628293.12 6583159.74 5628339.05 6583153.29 5628359.47 6583156.17 5628414.95 6583193.36 5628441.47 6583211.31 5628431.2 6583214.03 5628380.65 6583287.33 5628366.93 6583307.21 5628305.68 6583398.57</gml:posList>
                        </gml:LinearRing>
                    </gml:exterior>
                    <gml:interior>
                        <gml:LinearRing>
                            <gml:posList>5628134.1 6583260.22 5628189.4 6583298.62 5628199.02 6583305.24 5628209.26721486 6583311.92982567 5628230.8774056 6583280.28958155 5628227.39 6583277.88 5628211.5 6583266.86 5628157.66 6583229.32 5628153.38 6583234.97 5628134.1 6583260.22</gml:posList>
                        </gml:LinearRing>
                    </gml:interior>
                </gml:Polygon>
            </app:geometria>
            <app:plan xlink:href="https://www.gov.pl/zagospodarowanieprzestrzenne/app/AktPlanowaniaPrzestrzennego/PL.ZIPPZP.TEST/246401-POG/1POG"/>
            <app:nazwa xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/codelist/RodzajStrefyPlanistycznejKod/strefaZieleniIRekreacji" xlink:title="strefa zieleni i rekreacji"/>
            <app:profilPodstawowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-ZP" xlink:title="teren zieleni urządzonej"/>
            <app:profilPodstawowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-ZB" xlink:title="teren plaży"/>
            <app:profilPodstawowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-W" xlink:title="teren wód"/>
            <app:profilPodstawowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-K" xlink:title="teren komunikacji"/>
            <app:profilPodstawowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-ZD" xlink:title="teren ogrodów działkowych"/>
            <app:profilPodstawowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-I" xlink:title="teren infrastruktury technicznej"/>
            <app:profilDodatkowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-ZN" xlink:title="teren zieleni naturalnej"/>
            <app:profilDodatkowy xlink:href="https://www.gov.pl/static/zagospodarowanieprzestrzenne/ontology/KPT#KPT-MPZP-L" xlink:title="teren lasu"/>
            <app:maksNadziemnaIntensywnoscZabudowy>0.1</app:maksNadziemnaIntensywnoscZabudowy>
            <app:maksUdzialPowierzchniZabudowy>5.0</app:maksUdzialPowierzchniZabudowy>
            <app:maksWysokoscZabudowy uom="m">5.0</app:maksWysokoscZabudowy>
            <app:minUdzialPowierzchniBiologicznieCzynnej>70.0</app:minUdzialPowierzchniBiologicznieCzynnej>
        </app:StrefaPlanistyczna>
    </wfs:member>
    <wfs:member>
        <app:ObszarUzupelnieniaZabudowy gml:id="PL.ZIPPZP.TEST_246401-POG_1POG-58OUZ_20250903T125302">
            <gml:identifier codeSpace="https://www.gov.pl/zagospodarowanieprzestrzenne/app">https://www.gov.pl/zagospodarowanieprzestrzenne/app/ObszarUzupelnieniaZabudowy/PL.ZIPPZP.TEST/246401-POG/1POG-58OUZ/20250903T125302</gml:identifier>
            <app:idIIP>
                <app:Identyfikator>
                    <app:przestrzenNazw>PL.ZIPPZP.TEST/246401-POG</app:przestrzenNazw>
                    <app:lokalnyId>1POG-58OUZ</app:lokalnyId>
                    <app:wersjaId>20250903T125302</app:wersjaId>
                </app:Identyfikator>
            </app:idIIP>
            <app:nazwa>Obszar uzupełnienia zabudowy</app:nazwa>
            <app:oznaczenie>58OUZ</app:oznaczenie>
            <app:symbol>OUZ</app:symbol>
            <app:poczatekWersjiObiektu>2025-09-03T12:53:02Z</app:poczatekWersjiObiektu>
            <app:obowiazujeOd>2025-09-03</app:obowiazujeOd>
            <app:status xlink:href="http://inspire.ec.europa.eu/codelist/ProcessStepGeneralValue/elaboration" xlink:title="w opracowaniu"/>
            <app:charakterUstalenia xlink:href="https://inspire.ec.europa.eu/codelist/RegulationNatureValue/generallyBinding" xlink:title="ogólnie wiążące"/>
            <app:geometria>
                <gml:Polygon srsDimension="2" srsName="http://www.opengis.net/def/crs/EPSG/0/2177">
                    <gml:exterior>
                        <gml:LinearRing>
                            <gml:posList>5634365.83 6582091.69 5634438.38 6582074.27 5634443.45 6582095.04 5634370.52 6582112.56 5634365.83 6582091.69</gml:posList>
                        </gml:LinearRing>
                    </gml:exterior>
                </gml:Polygon>
            </app:geometria>
            <app:plan xlink:href="https://www.gov.pl/zagospodarowanieprzestrzenne/app/AktPlanowaniaPrzestrzennego/PL.ZIPPZP.TEST/246401-POG/1POG"/>
        </app:ObszarUzupelnieniaZabudowy>
    </wfs:member>
</wfs:FeatureCollection>
