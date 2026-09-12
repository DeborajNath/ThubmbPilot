import dev.localmouse.InputQueue;
import dev.localmouse.InputCommand;
import dev.localmouse.MotionAccumulator;
import java.util.concurrent.atomic.AtomicReference;

public class InputSmoke {
    static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
    public static void main(String[] args) throws Exception {
        long[] time = {0};
        InputQueue queue = new InputQueue(() -> time[0]);
        check(!queue.offer(new InputCommand.Click("left")), "Disconnected input accepted");
        queue.setEnabled(true);
        queue.offer(new InputCommand.Move(10, -5));
        queue.offer(new InputCommand.Move(3, 8));
        queue.offer(new InputCommand.Click("left"));
        queue.offer(new InputCommand.Move(1, 2));
        check(queue.take(0).equals(new InputCommand.Move(13, 3)), "Moves not coalesced");
        check(queue.take(0).equals(new InputCommand.Click("left")), "Click ordering lost");
        check(queue.take(0).equals(new InputCommand.Move(1, 2)), "Move crossed click barrier");
        queue.offer(new InputCommand.Move(400, -400));
        queue.offer(new InputCommand.Move(400, -400));
        check(queue.take(0).equals(new InputCommand.Move(512, -512)), "Movement cap failed");
        queue.offer(new InputCommand.Click("right"));
        queue.setEnabled(false); queue.setEnabled(true);
        check(queue.take(0) == null, "Input replayed across reconnect/pause");
        queue.offer(new InputCommand.Click("left")); time[0] += 251;
        check(queue.take(0) == null, "Stale input not discarded");
        for (int i = 0; i < 32; i++) check(queue.offer(new InputCommand.Click("left")), "Capacity too small");
        check(!queue.offer(new InputCommand.Click("right")), "Unbounded click queue");
        queue.setEnabled(false); queue.setEnabled(true);
        AtomicReference<InputCommand> received = new AtomicReference<>();
        Thread consumer = new Thread(() -> received.set(queue.take(1000)));
        consumer.start(); queue.offer(new InputCommand.Move(7, 9)); consumer.join(1500);
        check(!consumer.isAlive() && new InputCommand.Move(7, 9).equals(received.get()), "Input did not wake consumer");
        MotionAccumulator motion = new MotionAccumulator();
        motion.add(0.4, -0.4); check(motion.drain().equals(new InputCommand.Move(0, 0)), "Premature rounding");
        motion.add(0.4, -0.4); motion.drain();
        motion.add(0.4, -0.4); check(motion.drain().equals(new InputCommand.Move(1, -1)), "Fractional input lost");
        motion.reset(); check(motion.drain().equals(new InputCommand.Move(0, 0)), "Gesture cancellation did not reset");
        queue.setEnabled(false); queue.setEnabled(true);
        queue.offer(new InputCommand.Button(false)); time[0] += 1000;
        check(queue.take(0).equals(new InputCommand.Button(false)), "Button release expired");
        for (int i=0; i<32; i++) queue.offer(new InputCommand.Click("left"));
        check(queue.offer(new InputCommand.Button(false)), "Full queue lost button release");
        check(queue.take(0).equals(new InputCommand.Button(false)), "Release did not recover saturated queue");
        java.util.ArrayList<InputCommand> events = new java.util.ArrayList<>();
        dev.localmouse.GestureEngine gesture = new dev.localmouse.GestureEngine(c -> { events.add(c); return kotlin.Unit.INSTANCE; });
        gesture.down(0,0,1000); gesture.up(0,0,1050);
        gesture.down(0,0,1150); gesture.up(0,0,1200);
        check(events.equals(java.util.List.of(new InputCommand.Click("left"),new InputCommand.Button(true),new InputCommand.Button(false))), "Double tap sequence incorrect");
        events.clear(); gesture.cancel();
        gesture.down(0,0,2000); gesture.up(0,0,2050); gesture.down(0,0,2100);
        gesture.moveOne(20,10); gesture.up(20,10,2500);
        check(events.equals(java.util.List.of(new InputCommand.Click("left"), new InputCommand.Button(true), new InputCommand.Move(30,15), new InputCommand.Button(false))), "Drag order incorrect");
        events.clear(); gesture.cancel();
        gesture.down(0,0,3000); gesture.secondFinger(0,0,20,0,3040);
        gesture.moveTwo(0,20,20,20); gesture.fingerLift(3100); gesture.moveOne(100,100); gesture.up(100,100,3150);
        check(events.equals(java.util.List.of(new InputCommand.Scroll(0,60))), "Scroll emitted click or leftover-finger jump");
        events.clear(); gesture.down(0,0,4000); gesture.secondFinger(0,0,20,0,4040);
        gesture.fingerLift(4100); gesture.up(20,0,4120);
        check(events.equals(java.util.List.of(new InputCommand.Click("right"))), "Two-finger tap incorrect");
        events.clear(); gesture.down(0,0,5000); gesture.secondFinger(0,0,20,0,5040);
        gesture.moveTwo(-20,0,40,0); gesture.fingerLift(5100); gesture.up(40,0,5120);
        check(events.isEmpty(), "Pinch misclassified as right tap");
        gesture.setNaturalScroll(false); events.clear();
        gesture.down(0,0,6000); gesture.secondFinger(0,0,20,0,6040); gesture.moveTwo(20,20,40,20); gesture.cancel();
        check(events.equals(java.util.List.of(new InputCommand.Scroll(60,-60))), "Reversed scroll axes incorrect");
        events.clear(); gesture.down(0,0,7000); gesture.up(0,0,7050); gesture.down(0,0,7100); gesture.cancel(); gesture.cancel();
        check(events.equals(java.util.List.of(new InputCommand.Click("left"),new InputCommand.Button(true),new InputCommand.Button(false))), "Cancel failed to release exactly once");
        var textPackets = dev.localmouse.KeyboardCommands.text("a".repeat(255) + "😀" + "x");
        check(textPackets.size() == 2 && ((InputCommand.Text)textPackets.get(0)).getText().length() == 255 &&
            ((InputCommand.Text)textPackets.get(1)).getText().equals("😀x"), "Unicode chunk split a surrogate pair");
        var lines = dev.localmouse.KeyboardCommands.text("a".repeat(255) + "\r\n");
        check(((InputCommand.Text)lines.get(1)).getText().equals("\r\n"), "CRLF split across packets");
        check(dev.localmouse.KeyboardCommands.modified("c ",java.util.List.of("CTRL")).equals(java.util.List.of(new InputCommand.Key("C",java.util.List.of("CTRL")))), "Modifier key mapping failed");
        check(dev.localmouse.KeyboardCommands.modified("\n",java.util.List.of("CTRL")).equals(java.util.List.of(new InputCommand.Key("ENTER",java.util.List.of("CTRL")))), "Modified Enter mapping failed");
        for (String bad : java.util.List.of("\u0000", "\uD800", "x".repeat(2049))) {
            boolean rejected=false;
            try { dev.localmouse.KeyboardCommands.text(bad); } catch (IllegalArgumentException ex) { rejected=true; }
            check(rejected,"Invalid keyboard text accepted");
        }
        dev.localmouse.LiveComposition live = new dev.localmouse.LiveComposition();
        java.util.ArrayList<InputCommand> emitted = new java.util.ArrayList<>();
        kotlin.jvm.functions.Function1<java.util.List<? extends InputCommand>, Boolean> sender = batch -> { emitted.addAll(batch); return true; };
        live.update("h",sender); live.update("he",sender); live.update("hello",sender);
        check(emitted.equals(java.util.List.of(new InputCommand.Text("h"),new InputCommand.Text("e"),new InputCommand.Text("llo"))), "Live typing duplicated or buffered text");
        emitted.clear(); live.update("hello ",sender); live.forget();
        check(emitted.equals(java.util.List.of(new InputCommand.Text(" "))), "Commit duplicated word");
        live.update("teh",sender); emitted.clear(); live.update("the",sender);
        check(emitted.equals(java.util.List.of(new InputCommand.Key("BACKSPACE",java.util.List.of()),new InputCommand.Key("BACKSPACE",java.util.List.of()),new InputCommand.Text("he"))), "Correction delta failed");
        live.forget(); emitted.clear(); live.update("new",sender);
        check(emitted.equals(java.util.List.of(new InputCommand.Text("new"))), "Touchpad boundary retained old composition");
        check(!live.update("rejected", batch -> false) && live.getValue().equals("new"), "Rejected update changed state");
        live.forget(); live.update("😀",sender); emitted.clear(); live.update("😁",sender);
        check(emitted.equals(java.util.List.of(new InputCommand.Key("BACKSPACE",java.util.List.of()),new InputCommand.Text("😁"))), "Surrogate replacement failed");
        dev.localmouse.ImeDraft draft = new dev.localmouse.ImeDraft();
        draft.compose("h"); draft.compose("he"); draft.compose("hello");
        check(draft.commit("hello ").equals("hello ") && draft.finish().isEmpty(), "Composed word duplicated");
        draft.compose("नमस्ते"); check(draft.finish().equals("नमस्ते") && draft.finish().isEmpty(), "Finish composition duplicated text");
        draft.compose("unsent"); draft.clear(); check(draft.finish().isEmpty(), "Discarded draft replayed");
        queue.setEnabled(false); queue.setEnabled(true);
        check(queue.offerBatch(java.util.List.of(new InputCommand.Text("one"),new InputCommand.Key("ENTER",java.util.List.of()))),"Keyboard batch rejected");
        time[0] += 300;
        check(queue.take(0).equals(new InputCommand.Text("one")),"Typing silently expired at mouse timeout");
        check(queue.take(0).equals(new InputCommand.Key("ENTER",java.util.List.of())),"Text/key ordering lost");
        queue.offerBatch(java.util.List.of(new InputCommand.Text("late"))); time[0] += 2001;
        boolean stalled=false;
        try { queue.take(0); } catch (IllegalStateException ex) { stalled=true; }
        check(stalled,"Stalled typing not reported");
        queue.setEnabled(false); queue.setEnabled(true);
        for(int i=0;i<31;i++) queue.offer(new InputCommand.Click("left"));
        check(!queue.offerBatch(java.util.List.of(new InputCommand.Text("one"),new InputCommand.Text("two"))),"Partial keyboard batch accepted");
        for(int i=0;i<31;i++) queue.take(0);
        check(queue.take(0)==null,"Rejected batch partially enqueued");
        System.out.println("PASS: move coalescing, click order, movement bounds, pause/reconnect clearing, stale input expiry, bounded capacity, consumer wake-up and fractional precision, double tap, drag ordering/cancel, two-finger tap, scroll axes, pinch suppression, pointer transitions and release expiry/overflow protection, Unicode chunks, IME draft commit/finish/discard, modifier mapping and atomic typing queue/stall reporting.");
    }
}
