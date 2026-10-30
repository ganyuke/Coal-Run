using System;
using UnityEngine.Serialization;
using UnityEngine;

namespace CoalRun.Data
{
    // Every line of text the player reads, in one place. Edit Assets/Data/Lines.asset in the Inspector.
    // {0} and {1} are filled in by the game (a number, or the thing in the way); keep them in when rewording.
    [CreateAssetMenu(menuName = "Coal Run/Lines")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Core")]
    public class Lines : ScriptableObject
    {
        public Speech speech = new();
        public Phone phone = new();
        public Training training = new();
        public Tasks tasks = new();
        public Prompts prompts = new();
        public Levers levers = new();
        public Sheets sheets = new();
        public Pause pause = new();

        // What the operator says out loud.
        [Serializable]
        public class Speech
        {
            [Header("The shift")] public string freezing = "God, it's freezing in here. Gotta get the fire up.";

            public string shiftOver = "Shift's over. Into the staff lift.";
            public string fireOut = "Damn, the fire's out. Gotta light it again.";
            public string nothingToLight = "Nothing in the hopper to light. Need coal first.";
            public string fell = "Ow! That hurt.";

            [Header("The cart")] public string wheelOff = "Shit, a wheel came off!";

            public string cartLanding = "Hold on, the cart's still landing.";
            public string cartBuried = "The cart's buried. Gotta dig it out first.";
            public string tooMuchCoal = "Too much coal here to hold the cart. Dig a path.";
            public string handsFull = "My hands are full. Put the wheel down first.";
            public string cartNotLevel = "Hold on, let the cart come level first.";
            public string hookFirst = "Gotta hook the cart on first.";
            public string cartToCradle = "The cart needs to be at the tip cradle.";

            [Header("Fitting a wheel, when it can't be")]
            public string bothWheels = "It's already got both wheels.";

            public string notNow = "Not now.";
            public string offCradleFirst = "Gotta lower the cart off the cradle first.";
            public string standCartFirst = "Gotta stand the cart up first.";
            public string cartMoving = "The cart needs to stop moving first.";

            [Header("Out of reach")] public string tooFar = "Gotta get closer.";

            public string crankFar = "Gotta get to the wheel.";
            public string shovelFar = "Gotta get closer to the shovel.";

            [Header("The freight lift ({0}: cart, wheel, body)")]
            public string clearShaft = "Better get clear of the shaft.";

            [TextArea] public string underLift = "The {0}'s under the lift. Can't lower it.";
            public string standOnLift = "Gotta stand fully on the lift.";
            [TextArea] public string hangingOff = "The {0}'s hanging off the side. Can't raise it.";
        }

        // Brass upstairs, on the red phone.
        [Serializable]
        public class Phone
        {
            [Tooltip("Below 13° inside, said once a shift.")] [TextArea]
            public string cold = "“It’s getting cold upstairs. Could you turn the furnace up?”";

            [Tooltip("Below 7° inside, said once a shift.")] [TextArea]
            public string freezing = "“We can’t feel our hands. Forget the target. Just keep us alive.”";

            [Tooltip("At 0° inside, where the chill starts: said once a shift.")] [TextArea]
            public string frozen = "“It’s below zero up here. We can see our breath. Please, whatever it takes.”";

            [Tooltip("Back up to the cold line's temperature after it called, said once.")] [TextArea]
            public string warmed =
                "“Thanks for doing your job. It’s nice and cozy up here. We’ll be sure to pitch in to buy a blanket for you.”";

            [Tooltip("The first time a body goes in the furnace in a shift.")] [TextArea]
            public string burned = "“Our power just spiked. We don’t want to know why. Do it again.”";

            [Tooltip("After the first death, the second, and every one after.")] [TextArea]
            public string[] deaths =
            {
                "“We heard. We’re sending another guy down.”",
                "“Again? Fine. We’re sending another guy down.”",
                "“We’re sending another guy down. Please. Keep this one alive.”"
            };
        }

        // Day 1's training: the helper, the axle installer, and the notebook's steps.
        [Serializable]
        public class Training
        {
            [Header("The helper")] [TextArea] public string helperIntro =
                "“Alright newbie, just get up there and pull the lever. I’ll handle everything after this.”";

            [TextArea] public string helperReassure =
                "“No, it’ll be fine. I’ve done this hundreds of times. Just do it so we can get this done and report back to those pencil-pushers upstairs.”";

            [TextArea] public string helperGrease = "“Gotta put in a little more elbow grease.”";
            [TextArea] public string helperOw = "“Ow! Maybe I should’ve brought the hard hat today.”";
            [TextArea] public string helpDead = "“Well, the help is all dead. That’s awesome.”";

            [Header("The axle installer")] [TextArea]
            public string installerArrives =
                "“Engineering sent me down to fix your cart. Here’s the other axle. We stole it from the other guy’s cart. Give it a try.”";

            [Tooltip("The operator, having run him over with the cart.")] [TextArea]
            public string installerHit = "“Huh. It rolls.”";

            [Tooltip("Him, walking off to the staff lift once the cart's hauled round him.")]
            [TextArea]
            [FormerlySerializedAs("installerRunOver")]
            public string installerLeaves = "“I figured you’d run me over. Good luck down here.”";

            [Header("Notebook steps")] [TextArea]
            public string goPullLever = "Go up to the control room (W at the stairs) and pull the chute lever.";

            [TextArea] public string pullLever = "Pull the chute lever over with the mouse.";
            [TextArea] public string pullAgain = "Pull the lever again, all the way over.";
            [TextArea] public string stayUp = "Stay in the control room.";
            [TextArea] public string goDownDig = "Go down and dig the cart out.";
            [TextArea] public string grabShovel = "Grab the shovel by the furnace (E).";
            [TextArea] public string dig = "Dig the cart out. Stand at the coal and hold F.";
            [TextArea] public string keepDigging = "Keep digging until the cart is free.";

            [TextArea] public string haulUp =
                "Haul the cart onto the freight lift (hold Space and walk), then turn the crank to go up.";

            [TextArea] public string hook = "Push the cart to the tip cradle and hook it on with E.";
            [TextArea] public string tip = "Pull the tip lever to pour into the hopper.";
        }

        // The notebook's task during the shift.
        [Serializable]
        public class Tasks
        {
            [TextArea] public string fireOut = "The fire's out. Light it (hold E at the firebox)";
            [TextArea] public string fireOutEmpty = "The fire's out and the hopper's empty. Get coal to the furnace";
            [TextArea] public string standCart = "Stand the cart back up (E)";
            [TextArea] public string fixWheel = "Put the wheel back on (pick it up with E, then hold E at the cart)";
            [TextArea] public string digOut = "Dig the cart out (hold F at the coal)";
            [TextArea] public string fill = "Fill the cart at the chute";
            [TextArea] public string haul = "Take the cart up on the freight lift";
            [TextArea] public string hook = "Hook the cart on at the tip cradle (E)";
            [TextArea] public string tip = "Tip the cart into the hopper";
            [TextArea] public string lower = "Lower the cart (E)";
            [TextArea] public string returnCart = "Take the empty cart back down";

            [Tooltip(
                "{0}: the coldest it's heading for outside; {1}: the heat line, the coldest outside the fire keeps the building warm against at this setting.")]
            [TextArea]
            public string burnUp =
                "Turn the burn lever up: it's heading for {0:0}° outside, colder than the heat line ({1:0}°)";

            [TextArea] public string leave = "The shift is over. Stand in the staff lift";
        }

        // What the sticky notes say next to their key.
        [Serializable]
        public class Prompts
        {
            public string upStairs = "Up to the control room";
            public string downStairs = "Down the stairs";
            public string grip = "Grip cart";
            public string haul = "Haul the cart";
            public string heave = "Heave the skid up";
            public string letGo = "Let go to drop it";
            public string rightCart = "Right cart";
            public string hookCart = "Hook cart";
            public string lowerCart = "Lower cart";
            public string shovel = "Shovel";
            public string pickShovel = "Pick up the shovel";
            public string lightFire = "Light the fire";
            public string pickWheel = "Pick up wheel";
            public string fitWheel = "Fit wheel";
            public string putWheel = "Put wheel down";
            public string throwWheel = "Throw wheel";
            public string pickBody = "Pick up body";
            public string putBody = "Put body down";
            public string throwBody = "Throw body";
            public string crank = "Crank";
        }

        [Serializable]
        public class Lever
        {
            [Tooltip("On its sticky note.")] public string name;

            public string tooFar;

            [Tooltip("Said when the operator's on the other floor from it.")]
            public string wrongFloor;
        }

        [Serializable]
        public class Levers
        {
            public Lever chute = new()
            {
                name = "Chute lever", tooFar = "Gotta get closer to the lever.",
                wrongFloor = "That lever's up in the control room."
            };

            public Lever tip = new()
            {
                name = "Tip lever", tooFar = "Gotta get closer to the tip lever.",
                wrongFloor = "Gotta come out of the control room first."
            };

            public Lever burn = new()
            {
                name = "Burn lever", tooFar = "Gotta get closer to the burn lever.",
                wrongFloor = "Gotta come out of the control room first."
            };
        }

        // The pause card, opened with Esc: what the keys do, and how to leave.
        [Serializable]
        public class Pause
        {
            public string title = "PAUSED";

            [TextArea(6, 14)] public string controls =
                "A / D, arrows:  walk (tap twice to dash)\nW / S:  stairs at a door, crank at the lift\nSpace:  take hold of the cart (A / D to haul, Shift to heave)\nE:  pick up, put down, hook on (hold it to light the fire or fit a wheel)\nF:  shovel, aimed with the mouse\nMouse:  drag levers and the lift's wheel, click to throw";

            public string resume = "Resume   [Esc]";

            [Tooltip("Left out in a web build, which has nothing to quit.")]
            public string quit = "Quit   [Q]";
        }

        // The briefing and the report: the big notebook page in the middle.
        [Serializable]
        public class Sheets
        {
            [Tooltip("{0}: the day's number.")] public string day = "DAY {0:00}";

            public string briefing = "BRIEFING";
            public string report = "REPORT";

            [Tooltip("On the instrument panel's clock once the shift's done.")]
            public string clockOver = "OVER";

            [Header("Briefing (the day's memo goes on top)")] [TextArea]
            public string briefingHeadline = "Keep the building warm until the blizzard passes.";

            [TextArea(3, 6)] public string briefingBody =
                "Haul coal from the chute to the furnace. Feed it, light it, and set the burn lever: the colder it gets outside, the harder it has to burn.";

            public string targetLabel = "Electrical output target";
            public string oneWheel = "The cart has 1 wheel.";

            [Tooltip("{0}: how many.")] public string wheels = "The cart has {0} wheels.";

            public string begin = "Begin the shift   [Enter]";

            [Header("Report")] [TextArea] public string survived = "You kept them alive.";

            [Tooltip("Written under the headline of a survived shift, above the line about the output.")] [TextArea]
            public string survivedBody = "";

            [TextArea] public string lost = "No one is coming down.";

            [Tooltip("Written under the headline of a lost shift, above the line about the offices.")] [TextArea]
            public string lostBody = "";

            [TextArea] public string pleased = "Brass is pleased with the output.";
            [TextArea] public string notPleased = "Brass is not pleased with the output.";
            [TextArea] public string memoWaiting = "A memo is waiting for you in the morning.";
            [TextArea] public string lastShift = "That is all the shifts there are, for now.";
            [TextArea] public string frozeOver = "The offices froze first. There is nobody left to send a replacement.";
            public string outputLabel = "Electrical output";
            public string deliveredLabel = "Coal delivered";
            public string lostLabel = "Operators lost";
            public string bodiesLabel = "Bodies burned";

            [Tooltip("Added to the body of the report when anyone went in the furnace.")] [TextArea]
            public string bodiesNote = "Brass noticed the extra output and has chosen not to ask.";

            public string again = "Run the shift again   [R]";
            public string nextDay = "Next day   [R]";
            public string fromDay1 = "Play again from Day 1   [R]";
        }
    }
}