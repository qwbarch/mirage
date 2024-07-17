# import torch
# import transformers

# torch_version = torch.__version__
# print(f'Torch version: {torch_version}')

# from tqdm import tqdm
# from time import sleep

# t = tqdm(total=10)
# for i in range(10):
#     t.update(1)
#     sleep(1)

# print("Finished")


from sentence_transformers import SentenceTransformer, util
# model = SentenceTransformer("all-mpnet-base-v2")
# model = SentenceTransformer('sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2')

model = SentenceTransformer('model')

def foo():
    sentences = ["Hello", "World", "from", "Rust"]
    # Single list of sentences
    # sentences = [
    #     "The cat sits outside",
    #     "A man is playing guitar",
    #     "I love pasta",
    #     "The new movie is awesome",
    #     "The cat plays in the garden",
    #     "A woman watches TV",
    #     "The new movie is so great",
    #     "Do you like pizza?",
    #     "yo",
    #     "what's up",
    #     "are",
    #     "are you",
    #     "are you real",
    #     "are you good?",
    #     "hello",
    #     "hello hello",
    #     "leave",
    #     "Go in there.",
    #     "Turn left.",
    #     "Turn right.",
    #     "Go down the hallway.",
    #     "Kill yourself",
    #     "Shut up",
    #     "Die",
    #     "Alice: 'Hello there.' Bob: 'Go pick up the sponge.' Carol: 'Are you real?'",
    #     "'Hello there.' 'Go pick up the sponge.' 'Are you real?'",
    #     "'Are you real?' 'Go pick up the sponge.' 'Hello there.'",
    #     "Alice: 'Let's go to the movies.' Bob: 'Go pick up the sponge.' Carol: 'Delicious!'",
    #     "You good bro?",
    #     "You're inting",
    #     "i have crippling depression",
    #     "you have crippling depression",
    #     "Buy the stun gun",
    #     "(noises)",
    #     "what is that noise?",
    #     "what is that?",
    #     "drift all over the place",
    #     "are you real? apple",
    #     "are you real? robbery",
    #     "are you real? grab a flashlight",
    #     "are you real? yeah",
    #     "are you real? no",
    #     "are you real. apple",
    #     "are you real. robbery",
    #     "are you real. grab a flashlight",
    #     "are you real. yeah",
    #     "are you real. no",
    #     "are you real.",
    #     "are you real?",
    #     "are you are you are you are you",
    #     "are you are you are you are you are you are you are you",
    #     "are you",
    #     "你好",
    #     "很高兴见到你",
    #     "左转",
    #     "右转",
    #     "Tournez à droite",
    #     "Tournez à gauche",
    #     "bonjour",
    #     "how do i get there?",
    #     "how do i get there? how do i get there? how do i get there?",
    #     "how do i get there? go down the corridor and turn left.",
    #     "how do i get there? and that he supported the anarchist.",
    #     "how do i get there? he made many mistakes in the past.",
    #     "how do i get there? hello",
    #     "how do i get there? what's up brother",
    #     "how do i get? hello",
    # ]

    T = 0.4
    # T = -1

    # Compute embeddings
    embeddings = model.encode(sentences, convert_to_tensor=True, normalize_embeddings=True)
    print(embeddings)

    # Compute cosine-similarities for each sentence with each other sentence
    cosine_scores = util.cos_sim(embeddings, embeddings)

    # # Find the pairs with the highest cosine similarity scores
    # pairs = []
    # for i in range(len(cosine_scores)):
    #     for j in range(len(cosine_scores)):
    #         pairs.append({"index": [i, j], "score": cosine_scores[i][j]})

    # # Sort scores in decreasing order
    # # pairs = sorted(pairs, key=lambda x: x["score"], reverse=True)

    # for pair in pairs:
    #     i, j = pair["index"]
    #     print("{} \t\t {} \t\t Score: {:.4f}".format(
    #         sentences[i], sentences[j], pair["score"]
    #     ))

    for i in range(len(cosine_scores)):
        pairs = []
        for j in range(len(cosine_scores)):
            pairs.append({"index": [i, j], "score": cosine_scores[i][j]})
        pairs = sorted(pairs, key=lambda x: x["score"], reverse=True)

        for pair in pairs:
            if (pair["score"] < T):
                continue

            y, z = pair["index"]
            print("{} \t\t {} \t\t Score: {:.4f}".format(
                sentences[y], sentences[z], pair["score"]
            ))

foo()