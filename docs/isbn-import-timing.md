# Medição da importação por ISBN

Ensaio local com SQLite e 20 ISBNs distintos. Os fornecedores de teste simulam latências diferentes entre livros e duas pesquisas de metadados sem resultados. A duração começa no envio do lote e termina quando o estado final é observado na base de dados. O primeiro livro é registado quando fica visível nessa mesma base de dados. O teste consulta a base de dados a cada 10 ms.

| Medida | Antes | Depois |
| --- | ---: | ---: |
| Duração do lote | 5,536 s | 5,303 s |
| Tempo até ao primeiro livro | 1,593 s | 1,614 s |
| Espera média por ISBN até iniciar o processamento | 2,959 s | 2,945 s |
| Tempo total de pesquisa de metadados | 0,692 s | 0,648 s |
| Tempo total de obtenção de capas | 1,285 s | 1,172 s |
| Pedidos de capa | 20 | 18 |
| ISBNs com falha | 2 | 2 |

A importação passou a saltar o pedido de capa quando a pesquisa de metadados falha. Neste ensaio, o lote terminou 233 ms mais cedo, com 18 livros distintos gravados nas duas execuções. O primeiro livro demorou mais 21 ms na segunda execução. A espera por ISBN inclui o processamento dos livros anteriores no lote e tem intervalos sobrepostos.

Guardar primeiro o livro retiraria do seu percurso de publicação a espera pela capa, que foi de 65 ms por pedido após a alteração, em média. Para publicar livros sem capa e continuar a tentar obtê-la após um reinício, seria necessário persistir esse trabalho e definir como uma falha de capa afeta o estado de importação apresentado. As medições usam fornecedores simulados. Ainda falta medir a distribuição de tempos com os fornecedores reais antes de decidir sobre concorrência por fornecedor ou processamento separado de capas.

O teste pode ser repetido com o comando abaixo. Para obter tempos num ambiente da aplicação, recolher os histogramas do medidor `Bibliotheca.IsbnImport`. O teste de reinício da suite HTTP verifica também que o trabalhador retoma o ISBN seguinte de um lote interrompido.

```powershell
dotnet test Bibliotheca.slnx --filter 'Category=ImportTiming' --logger 'console;verbosity=detailed'
```
