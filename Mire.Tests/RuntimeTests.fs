/// Runtime lifecycle pieces that can be exercised without a terminal: the async
/// command supervisor (error delivery, cancellation, late sends) and the bounded
/// message pump (fairness under a self-rescheduling chain, prompt quit).
module Mire.Tests.RuntimeTests

open System
open System.Threading
open Expecto
open Mire.App

/// Poll `cond` until it holds or `timeoutMs` passes.
let private waitFor (timeoutMs: int) (cond: unit -> bool) =
    let sw = Diagnostics.Stopwatch.StartNew()

    while not (cond ()) && sw.ElapsedMilliseconds < int64 timeoutMs do
        Thread.Sleep 5

    cond ()

[<Tests>]
let supervisorTests =
    testList
        "CommandSupervisor"
        [ test "an exception thrown after an async yield is delivered exactly once" {
              use sup = new CommandSupervisor<int>(ignore)

              let cmd: Cmd<int> =
                  Cmd.ofAsync (fun _ ->
                      async {
                          do! Async.Sleep 10
                          failwith "boom"
                      })

              Cmd.dispatchWith ignore ignore sup.Send sup.Start cmd
              Expect.isTrue (waitFor 2000 (fun () -> sup.ActiveCount = 0)) "command finished"

              match sup.TryTakeError() with
              | Some ex -> Expect.equal ex.Message "boom" "the async failure surfaced"
              | None -> failtest "expected the error to be queued"

              Expect.isNone (sup.TryTakeError()) "delivered once"
          }
          test "an exception thrown while *building* the async is also supervised" {
              use sup = new CommandSupervisor<int>(ignore)
              let cmd: Cmd<int> = Cmd.ofAsync (fun _ -> failwith "eager")
              Cmd.dispatchWith ignore ignore sup.Send sup.Start cmd
              Expect.isTrue (waitFor 2000 (fun () -> sup.ActiveCount = 0)) "finished"
              Expect.isSome (sup.TryTakeError()) "eager failure queued, not thrown into dispatch"
          }
          test "Stop cancels a sleeping command without reporting an error" {
              use sup = new CommandSupervisor<int>(ignore)
              let mutable finishedNormally = false

              let cmd: Cmd<int> =
                  Cmd.ofAsync (fun _ ->
                      async {
                          do! Async.Sleep 60_000
                          finishedNormally <- true
                      })

              Cmd.dispatchWith ignore ignore sup.Send sup.Start cmd
              Expect.equal sup.ActiveCount 1 "running"
              sup.Stop()
              Expect.isTrue (waitFor 2000 (fun () -> sup.ActiveCount = 0)) "cancelled promptly"
              Expect.isFalse finishedNormally "never ran to completion"
              Expect.isNone (sup.TryTakeError()) "cancellation is not an error"
          }
          test "the ambient token is the runtime's: commands can observe cancellation" {
              use sup = new CommandSupervisor<int>(ignore)
              let mutable sawToken = CancellationToken.None

              Cmd.dispatchWith
                  ignore
                  ignore
                  sup.Send
                  sup.Start
                  (Cmd.ofAsync (fun _ ->
                      async {
                          let! ct = Async.CancellationToken
                          sawToken <- ct
                      }))

              Expect.isTrue (waitFor 2000 (fun () -> sup.ActiveCount = 0)) "ran"
              sup.Stop()
              Expect.isTrue sawToken.IsCancellationRequested "the command's token is cancelled by Stop"
          }
          test "sends after Stop are dropped; nothing new starts" {
              let got = Collections.Concurrent.ConcurrentQueue<int>()
              use sup = new CommandSupervisor<int>(got.Enqueue)
              sup.Send 1
              sup.Stop()
              sup.Send 2
              let mutable started = false
              sup.Start(async { started <- true })
              Thread.Sleep 50
              Expect.equal (List.ofSeq got) [ 1 ] "late send dropped"
              Expect.isFalse started "no start after Stop"
          }
          test "a late send from a command that outlives Stop is dropped" {
              let got = Collections.Concurrent.ConcurrentQueue<int>()
              use sup = new CommandSupervisor<int>(got.Enqueue)
              use gate = new ManualResetEventSlim(false)

              // Blocking (non-cooperative) work: cancellation can't interrupt it,
              // so it genuinely sends after the runtime stopped.
              Cmd.dispatchWith
                  ignore
                  ignore
                  sup.Send
                  sup.Start
                  (Cmd.ofAsync (fun send ->
                      async {
                          gate.Wait()
                          send 42
                      }))

              sup.Stop()
              gate.Set()
              Expect.isTrue (waitFor 2000 (fun () -> sup.ActiveCount = 0)) "finished"
              Expect.isEmpty (List.ofSeq got) "late send never reached the queue"
          } ]

[<Tests>]
let pumpTests =
    testList
        "MessagePump"
        [ test "a self-rescheduling chain is bounded per pump, so frames can interleave" {
              let q = Collections.Generic.Queue<int>()
              q.Enqueue 0
              let limit = 100_000

              let tryDequeue () =
                  if q.Count > 0 then Some(q.Dequeue()) else None

              // Each message schedules the next (like `update` returning Cmd.ofMsg).
              let handle n =
                  if n < limit then
                      q.Enqueue(n + 1)

              let mutable pumps = 0
              let mutable total = 0

              while q.Count > 0 do
                  total <-
                      total
                      + MessagePump.run tryDequeue handle (fun () -> false) 1000 (TimeSpan.FromSeconds 5.0)

                  pumps <- pumps + 1

              Expect.equal total (limit + 1) "every message handled exactly once"
              Expect.isGreaterThan pumps 50 "the chain spanned many pumps (intermediate frames possible)"
          }
          test "the time budget bounds a pump even with a huge message allowance" {
              let tryDequeue () = Some 1
              let handle _ = Thread.SpinWait 1000

              let sw = Diagnostics.Stopwatch.StartNew()

              let n =
                  MessagePump.run tryDequeue handle (fun () -> false) Int32.MaxValue (TimeSpan.FromMilliseconds 20.0)

              Expect.isLessThan sw.ElapsedMilliseconds 2000L "returned near the budget"
              Expect.isGreaterThan n 0 "made progress"
          }
          test "a quit request mid-pump stops the pump promptly" {
              let mutable quit = false
              let tryDequeue () = Some 1
              let mutable handled = 0

              let handle _ =
                  handled <- handled + 1

                  if handled = 10 then
                      quit <- true

              let n =
                  MessagePump.run tryDequeue handle (fun () -> quit) Int32.MaxValue (TimeSpan.FromSeconds 5.0)

              Expect.equal n 10 "stopped right after the quitting message"
          } ]
